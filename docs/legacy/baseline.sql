-- Adopts a database that the legacy app (eShopLegacyMVC) created, so that the new API can use it
-- (ADR-0012). The procedure is in docs/legacy/README.md, under Adopting an existing legacy database.
--
-- What it does: it records the InitialCreate migration as applied, because a legacy database
-- already has the schema that InitialCreate creates. EF Core then starts from the next migration.
-- It only adds EF Core's history table and one row. Every legacy object stays as it is: EF6's
-- [__MigrationHistory], the unused [catalog_brand_hilo] and [catalog_type_hilo] sequences, and all data.
--
-- What it checks first, refusing the database with an error number when a check fails:
--   50001  it runs inside a transaction, or with IMPLICIT_TRANSACTIONS on, either of which could
--          still roll it back. The caller's transaction is left as it was.
--   50002  the server has no sys.sequences.last_used_value: SQL Server older than 2017
--   50012  the login lacks VIEW DEFINITION on the database, so the checks below could not see
--          every object
--   50003  it cannot take EF Core's migrations lock within 30 seconds
--   50004  dbo.__EFMigrationsHistory exists but is not the table EF Core creates
--   50005  dbo.__EFMigrationsHistory holds other migrations but not InitialCreate
--   50006  the legacy catalog tables or the catalog_hilo sequence are missing
--   50007  the columns of the catalog tables differ from the legacy schema
--   50008  the keys, indexes, statistics, constraints or triggers of the catalog tables, the
--          foreign keys into them, or schema-bound objects on them differ from the legacy schema
--   50009  catalog_hilo is not a bigint sequence with INCREMENT BY 10 and NO CYCLE
--   50010  an item ID is at or above the next value of catalog_hilo, or the sequence cannot
--          hand out a whole block of int item IDs
--   50011  the brands or types differ from the reference data that InitialCreate seeds
-- Running it again, or against a database that the migrations created, changes nothing.
--
-- One batch without GO separators, sqlcmd commands or variables, or double-quoted identifiers, so
-- that sqlcmd and SqlClient run it the same way. Statements that read columns of the catalog tables
-- run through sp_executesql: in the batch itself, a renamed column would stop the whole batch at
-- compile time, before the column check could report it. For the same reason nothing here needs a
-- newer server than the version check allows.

-- Checked before XACT_ABORT is on, so that a refusal leaves the caller's transaction alone.
IF @@TRANCOUNT > 0 OR (@@OPTIONS & 2) = 2
    THROW 50001, N'Run the baseline outside a transaction and with IMPLICIT_TRANSACTIONS off: an outer transaction could still roll it back. Your transaction was left as it was.', 1;

SET XACT_ABORT ON;
SET NOCOUNT ON;
SET LOCK_TIMEOUT 30000;

IF COL_LENGTH(N'sys.sequences', N'last_used_value') IS NULL
    THROW 50002, N'The baseline needs sys.sequences.last_used_value: SQL Server 2017 or later, or Azure SQL.', 1;

-- Without VIEW DEFINITION, sys.sql_expression_dependencies and similar views show the login nothing,
-- and the checks below would pass objects they cannot see.
IF HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DEFINITION') = 0
    THROW 50012, N'The login cannot see every object in the database: grant it VIEW DEFINITION on the database, or use db_owner.', 1;

BEGIN TRANSACTION;

-- Migrate() and dotnet ef database update take this lock, so they wait for the baseline and it
-- waits for them. The idempotent migrations script takes no lock: the procedure runs the baseline first.
DECLARE @lock int;
EXEC @lock = sp_getapplock @Resource = N'__EFMigrationsLock', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @lock < 0
    THROW 50003, N'Could not take EF Core''s migrations lock (__EFMigrationsLock) within 30 seconds: a migration or another baseline is running.', 1;

DECLARE @message nvarchar(2048);
DECLARE @differences TABLE (fact nvarchar(400) NOT NULL);

-- 1. EF Core's history table. An empty one is what a migration leaves behind when it fails on the
--    legacy objects; the database is then still waiting for its baseline.
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NOT NULL
BEGIN
    DECLARE @expectedHistory TABLE (fact nvarchar(400) NOT NULL);
    INSERT @expectedHistory (fact) VALUES
        (N'column MigrationId nvarchar(150) not null'),
        (N'column ProductVersion nvarchar(32) not null'),
        (N'index PK___EFMigrationsHistory primary key clustered'),
        (N'index PK___EFMigrationsHistory key 1 MigrationId asc');

    DECLARE @actualHistory TABLE (fact nvarchar(400) NOT NULL);
    INSERT @actualHistory (fact)
    SELECT CONCAT(N'column ', c.name, N' ',
        CASE
            WHEN ty.name IN (N'nvarchar', N'nchar') THEN CONCAT(ty.name, N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS nvarchar(10)) END, N')')
            WHEN ty.name IN (N'varchar', N'char', N'varbinary', N'binary') THEN CONCAT(ty.name, N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS nvarchar(10)) END, N')')
            WHEN ty.name IN (N'decimal', N'numeric') THEN CONCAT(ty.name, N'(', c.precision, N',', c.scale, N')')
            ELSE ty.name
        END,
        CASE WHEN c.is_nullable = 1 THEN N' null' ELSE N' not null' END)
    FROM sys.columns AS c JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'[dbo].[__EFMigrationsHistory]')
    UNION ALL
    SELECT CONCAT(N'index ', i.name, CASE WHEN i.is_primary_key = 1 THEN N' primary key ' ELSE N' ' END, LOWER(i.type_desc))
    FROM sys.indexes AS i WHERE i.object_id = OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') AND i.type > 0
    UNION ALL
    SELECT CONCAT(N'index ', i.name, N' key ', ic.key_ordinal, N' ', COL_NAME(ic.object_id, ic.column_id), CASE WHEN ic.is_descending_key = 1 THEN N' desc' ELSE N' asc' END)
    FROM sys.indexes AS i JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.object_id = OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') AND i.type > 0;

    INSERT @differences (fact)
    SELECT N'missing: ' + fact FROM (SELECT fact FROM @expectedHistory EXCEPT SELECT fact FROM @actualHistory) AS m
    UNION ALL
    SELECT N'unexpected: ' + fact FROM (SELECT fact FROM @actualHistory EXCEPT SELECT fact FROM @expectedHistory) AS u;

    IF EXISTS (SELECT * FROM @differences)
    BEGIN
        SELECT @message = CONCAT(@message, N'; ', fact) FROM @differences ORDER BY fact;
        SET @message = LEFT(N'dbo.__EFMigrationsHistory is not the table EF Core creates: ' + STUFF(@message, 1, 2, N''), 2048);
        THROW 50004, @message, 1;
    END;

    DECLARE @applied int, @others int;
    EXEC sp_executesql
        N'SELECT @applied = ISNULL(SUM(CASE WHEN [MigrationId] = N''20260929065225_InitialCreate'' THEN 1 ELSE 0 END), 0),
                 @others = ISNULL(SUM(CASE WHEN [MigrationId] = N''20260929065225_InitialCreate'' THEN 0 ELSE 1 END), 0)
          FROM [dbo].[__EFMigrationsHistory];',
        N'@applied int OUTPUT, @others int OUTPUT', @applied = @applied OUTPUT, @others = @others OUTPUT;

    IF @applied = 1
    BEGIN
        COMMIT;
        PRINT N'InitialCreate is already recorded as applied: nothing to do.';
        RETURN;
    END;

    IF @others > 0
        THROW 50005, N'dbo.__EFMigrationsHistory holds other migrations but not InitialCreate: this is not a legacy database waiting for its baseline.', 1;
END;

-- 2. The legacy objects exist.
IF OBJECT_ID(N'[dbo].[Catalog]', N'U') IS NULL
    OR OBJECT_ID(N'[dbo].[CatalogBrand]', N'U') IS NULL
    OR OBJECT_ID(N'[dbo].[CatalogType]', N'U') IS NULL
    OR OBJECT_ID(N'[dbo].[catalog_hilo]', N'SO') IS NULL
    THROW 50006, N'Not a legacy catalog database: dbo.Catalog, dbo.CatalogBrand, dbo.CatalogType or dbo.catalog_hilo is missing. Check the database name.', 1;

-- 3. The columns, both ways: a missing column breaks reads, and an extra NOT NULL column breaks
--    inserts. A collation is compared only where it differs from the database's, because EF6 and
--    the migrations both take the database default.
DECLARE @tables TABLE (object_id int NOT NULL);
INSERT @tables (object_id) VALUES (OBJECT_ID(N'[dbo].[Catalog]')), (OBJECT_ID(N'[dbo].[CatalogBrand]')), (OBJECT_ID(N'[dbo].[CatalogType]'));

DECLARE @expectedColumns TABLE (fact nvarchar(400) NOT NULL);
INSERT @expectedColumns (fact) VALUES
    (N'Catalog.Id int not null'),
    (N'Catalog.Name nvarchar(50) not null'),
    (N'Catalog.Description nvarchar(max) null'),
    (N'Catalog.Price decimal(18,2) not null'),
    (N'Catalog.PictureFileName nvarchar(max) not null'),
    (N'Catalog.CatalogTypeId int not null'),
    (N'Catalog.CatalogBrandId int not null'),
    (N'Catalog.AvailableStock int not null'),
    (N'Catalog.RestockThreshold int not null'),
    (N'Catalog.MaxStockThreshold int not null'),
    (N'Catalog.OnReorder bit not null'),
    (N'CatalogBrand.Id int identity(1,1) not null'),
    (N'CatalogBrand.Brand nvarchar(100) not null'),
    (N'CatalogType.Id int identity(1,1) not null'),
    (N'CatalogType.Type nvarchar(100) not null');

DECLARE @databaseCollation sysname = CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS sysname);

DECLARE @actualColumns TABLE (fact nvarchar(400) NOT NULL);
INSERT @actualColumns (fact)
SELECT CONCAT(
    OBJECT_NAME(c.object_id), N'.', c.name, N' ',
    CASE
        WHEN ty.name IN (N'nvarchar', N'nchar') THEN CONCAT(ty.name, N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS nvarchar(10)) END, N')')
        WHEN ty.name IN (N'varchar', N'char', N'varbinary', N'binary') THEN CONCAT(ty.name, N'(', CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS nvarchar(10)) END, N')')
        WHEN ty.name IN (N'decimal', N'numeric') THEN CONCAT(ty.name, N'(', c.precision, N',', c.scale, N')')
        ELSE ty.name
    END,
    CASE WHEN c.collation_name IS NULL OR c.collation_name = @databaseCollation THEN N'' ELSE CONCAT(N' collate ', c.collation_name) END,
    CASE WHEN ic.object_id IS NULL THEN N'' ELSE CONCAT(N' identity(', CAST(ic.seed_value AS bigint), N',', CAST(ic.increment_value AS bigint), N')') END,
    CASE WHEN c.is_nullable = 1 THEN N' null' ELSE N' not null' END,
    CASE WHEN c.is_computed = 1 THEN N' computed' ELSE N'' END,
    CASE WHEN c.default_object_id <> 0 THEN N' default' ELSE N'' END)
FROM sys.columns AS c
JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.identity_columns AS ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE c.object_id IN (SELECT object_id FROM @tables);

INSERT @differences (fact)
SELECT N'missing: ' + fact FROM (SELECT fact FROM @expectedColumns EXCEPT SELECT fact FROM @actualColumns) AS m
UNION ALL
SELECT N'unexpected: ' + fact FROM (SELECT fact FROM @actualColumns EXCEPT SELECT fact FROM @expectedColumns) AS u;

IF EXISTS (SELECT * FROM @differences)
BEGIN
    SELECT @message = CONCAT(@message, N'; ', fact) FROM @differences ORDER BY fact;
    SET @message = LEFT(N'The columns of the catalog tables differ from the legacy schema: ' + STUFF(@message, 1, 2, N''), 2048);
    THROW 50007, @message, 1;
END;

-- 4. Everything else on the catalog tables, and what refers to them, both ways and in detail: a
--    key or index by kind, clustering, columns and state, a foreign key by columns, actions and
--    state. After the baseline the migrations own the schema, and they know only these objects:
--    a later migration that renames or drops one needs it as it is, and an unknown index or
--    statistic can stop a later column change. An enabled trigger also breaks EF Core's writes,
--    which use OUTPUT without INTO.
DECLARE @expectedObjects TABLE (fact nvarchar(400) NOT NULL);
INSERT @expectedObjects (fact) VALUES
    (N'dbo.Catalog primary key PK_dbo.Catalog clustered'),
    (N'dbo.Catalog primary key PK_dbo.Catalog key 1 Id asc'),
    (N'dbo.CatalogBrand primary key PK_dbo.CatalogBrand clustered'),
    (N'dbo.CatalogBrand primary key PK_dbo.CatalogBrand key 1 Id asc'),
    (N'dbo.CatalogType primary key PK_dbo.CatalogType clustered'),
    (N'dbo.CatalogType primary key PK_dbo.CatalogType key 1 Id asc'),
    (N'dbo.Catalog index IX_CatalogBrandId nonclustered'),
    (N'dbo.Catalog index IX_CatalogBrandId key 1 CatalogBrandId asc'),
    (N'dbo.Catalog index IX_CatalogTypeId nonclustered'),
    (N'dbo.Catalog index IX_CatalogTypeId key 1 CatalogTypeId asc'),
    (N'dbo.Catalog foreign key FK_dbo.Catalog_dbo.CatalogBrand_CatalogBrandId references dbo.CatalogBrand on delete CASCADE on update NO ACTION'),
    (N'dbo.Catalog foreign key FK_dbo.Catalog_dbo.CatalogBrand_CatalogBrandId column 1 CatalogBrandId references Id'),
    (N'dbo.Catalog foreign key FK_dbo.Catalog_dbo.CatalogType_CatalogTypeId references dbo.CatalogType on delete CASCADE on update NO ACTION'),
    (N'dbo.Catalog foreign key FK_dbo.Catalog_dbo.CatalogType_CatalogTypeId column 1 CatalogTypeId references Id');

DECLARE @actualObjects TABLE (fact nvarchar(400) NOT NULL);
INSERT @actualObjects (fact)
SELECT CONCAT(
    OBJECT_SCHEMA_NAME(i.object_id), N'.', OBJECT_NAME(i.object_id),
    CASE WHEN i.is_primary_key = 1 THEN N' primary key ' WHEN i.is_unique_constraint = 1 THEN N' unique constraint ' WHEN i.is_unique = 1 THEN N' unique index ' ELSE N' index ' END,
    i.name, N' ', LOWER(i.type_desc),
    CASE WHEN i.has_filter = 1 THEN CONCAT(N' where ', i.filter_definition) ELSE N'' END,
    CASE WHEN i.is_disabled = 1 THEN N' disabled' ELSE N'' END)
FROM sys.indexes AS i WHERE i.object_id IN (SELECT object_id FROM @tables) AND i.type > 0
UNION ALL
SELECT CONCAT(
    OBJECT_SCHEMA_NAME(i.object_id), N'.', OBJECT_NAME(i.object_id),
    CASE WHEN i.is_primary_key = 1 THEN N' primary key ' WHEN i.is_unique_constraint = 1 THEN N' unique constraint ' WHEN i.is_unique = 1 THEN N' unique index ' ELSE N' index ' END,
    i.name,
    CASE WHEN ic.is_included_column = 1 THEN CONCAT(N' includes ', COL_NAME(ic.object_id, ic.column_id))
         ELSE CONCAT(N' key ', ic.key_ordinal, N' ', COL_NAME(ic.object_id, ic.column_id), CASE WHEN ic.is_descending_key = 1 THEN N' desc' ELSE N' asc' END) END)
FROM sys.indexes AS i JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
WHERE i.object_id IN (SELECT object_id FROM @tables) AND i.type > 0
UNION ALL
SELECT CONCAT(
    OBJECT_SCHEMA_NAME(fk.parent_object_id), N'.', OBJECT_NAME(fk.parent_object_id), N' foreign key ', fk.name,
    N' references ', OBJECT_SCHEMA_NAME(fk.referenced_object_id), N'.', OBJECT_NAME(fk.referenced_object_id),
    N' on delete ', REPLACE(fk.delete_referential_action_desc, N'_', N' '),
    N' on update ', REPLACE(fk.update_referential_action_desc, N'_', N' '),
    CASE WHEN fk.is_disabled = 1 THEN N' disabled' ELSE N'' END,
    CASE WHEN fk.is_not_trusted = 1 THEN N' not trusted' ELSE N'' END)
FROM sys.foreign_keys AS fk WHERE fk.parent_object_id IN (SELECT object_id FROM @tables) OR fk.referenced_object_id IN (SELECT object_id FROM @tables)
UNION ALL
SELECT CONCAT(
    OBJECT_SCHEMA_NAME(fk.parent_object_id), N'.', OBJECT_NAME(fk.parent_object_id), N' foreign key ', fk.name,
    N' column ', fkc.constraint_column_id, N' ', COL_NAME(fkc.parent_object_id, fkc.parent_column_id),
    N' references ', COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id))
FROM sys.foreign_keys AS fk JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
WHERE fk.parent_object_id IN (SELECT object_id FROM @tables) OR fk.referenced_object_id IN (SELECT object_id FROM @tables)
UNION ALL
SELECT CONCAT(OBJECT_SCHEMA_NAME(o.parent_object_id), N'.', OBJECT_NAME(o.parent_object_id), N' ', LOWER(REPLACE(o.type_desc, N'_', N' ')), N' ', o.name)
FROM sys.objects AS o WHERE o.parent_object_id IN (SELECT object_id FROM @tables) AND o.type IN ('C', 'D', 'TR')
UNION ALL
SELECT CONCAT(OBJECT_SCHEMA_NAME(s.object_id), N'.', OBJECT_NAME(s.object_id), N' statistics ', s.name)
FROM sys.stats AS s WHERE s.object_id IN (SELECT object_id FROM @tables) AND s.user_created = 1
UNION ALL
SELECT DISTINCT CONCAT(OBJECT_SCHEMA_NAME(d.referenced_id), N'.', OBJECT_NAME(d.referenced_id), N' schema-bound dependent ', OBJECT_SCHEMA_NAME(d.referencing_id), N'.', OBJECT_NAME(d.referencing_id))
FROM sys.sql_expression_dependencies AS d WHERE d.referenced_id IN (SELECT object_id FROM @tables) AND d.is_schema_bound_reference = 1;

INSERT @differences (fact)
SELECT N'missing: ' + fact FROM (SELECT fact FROM @expectedObjects EXCEPT SELECT fact FROM @actualObjects) AS m
UNION ALL
SELECT N'unexpected: ' + fact FROM (SELECT fact FROM @actualObjects EXCEPT SELECT fact FROM @expectedObjects) AS u;

IF EXISTS (SELECT * FROM @differences)
BEGIN
    SELECT @message = CONCAT(@message, N'; ', fact) FROM @differences ORDER BY fact;
    SET @message = LEFT(N'The keys, indexes, constraints or triggers of the catalog tables differ from the legacy schema: ' + STUFF(@message, 1, 2, N''), 2048);
    THROW 50008, @message, 1;
END;

-- 5. The item-ID sequence. Both the legacy generator and EF Core hand out blocks of 10 without
--    reading the increment, so it must be 10. Its next value must lie above every item ID, or a
--    block would contain an ID that is already taken, and the whole next block must fit in the
--    sequence and in the int item ID.
IF NOT EXISTS (
    SELECT * FROM sys.sequences
    WHERE object_id = OBJECT_ID(N'[dbo].[catalog_hilo]') AND TYPE_NAME(user_type_id) = N'bigint'
        AND CAST(increment AS bigint) = 10 AND is_cycling = 0)
    THROW 50009, N'dbo.catalog_hilo is not a bigint sequence with INCREMENT BY 10 and NO CYCLE, so HiLo could hand out duplicate item IDs.', 1;

DECLARE @next decimal(20, 0), @last decimal(20, 0), @exhausted bit, @maxId int;
EXEC sp_executesql
    N'SELECT @next = CASE WHEN last_used_value IS NULL THEN CAST(current_value AS decimal(20, 0)) ELSE CAST(current_value AS decimal(20, 0)) + CAST(increment AS decimal(20, 0)) END,
             @last = CAST(maximum_value AS decimal(20, 0)),
             @exhausted = is_exhausted
      FROM sys.sequences WHERE object_id = OBJECT_ID(N''[dbo].[catalog_hilo]'');
      SELECT @maxId = MAX([Id]) FROM [dbo].[Catalog];',
    N'@next decimal(20, 0) OUTPUT, @last decimal(20, 0) OUTPUT, @exhausted bit OUTPUT, @maxId int OUTPUT',
    @next = @next OUTPUT, @last = @last OUTPUT, @exhausted = @exhausted OUTPUT, @maxId = @maxId OUTPUT;

IF @maxId >= @next
BEGIN
    SET @message = CONCAT(
        N'dbo.catalog_hilo would next return ', @next, N', but dbo.Catalog already has item ID ', @maxId,
        N'. Restart the sequence above the highest ID first, with ALTER SEQUENCE dbo.catalog_hilo RESTART WITH ',
        (@maxId / 10 + 1) * 10 + 1, N', while no app is running.');
    THROW 50010, @message, 1;
END;

IF @exhausted = 1 OR @next + 9 > @last OR @next + 9 > 2147483647
BEGIN
    SET @message = CONCAT(
        N'dbo.catalog_hilo cannot hand out the next block of 10 item IDs from ', @next,
        N': the sequence is exhausted or ends at ', @last, N', or the IDs would pass the int range of dbo.Catalog.Id.');
    THROW 50010, @message, 1;
END;

-- 6. The reference data, both ways and byte for byte: the collation would ignore case and trailing
--    spaces. Later migrations that change reference data assume exactly these rows.
INSERT @differences (fact)
EXEC sp_executesql N'
    WITH expected AS (
        SELECT N''brand'' AS kind, Id, CONVERT(varbinary(400), Name) AS Name FROM (VALUES
            (1, N''Azure''), (2, N''.NET''), (3, N''Visual Studio''), (4, N''SQL Server''), (5, N''Other'')) AS v (Id, Name)
        UNION ALL
        SELECT N''type'', Id, CONVERT(varbinary(400), Name) FROM (VALUES
            (1, N''Mug''), (2, N''T-Shirt''), (3, N''Sheet''), (4, N''USB Memory Stick'')) AS v (Id, Name)),
    actual AS (
        SELECT N''brand'' AS kind, [Id], CONVERT(varbinary(400), [Brand]) AS Name FROM [dbo].[CatalogBrand]
        UNION ALL
        SELECT N''type'', [Id], CONVERT(varbinary(400), [Type]) FROM [dbo].[CatalogType])
    SELECT CONCAT(N''missing: '', kind, N'' '', Id, N'' '', CONVERT(nvarchar(200), Name)) FROM (SELECT * FROM expected EXCEPT SELECT * FROM actual) AS m
    UNION ALL
    SELECT CONCAT(N''unexpected: '', kind, N'' '', Id, N'' '', CONVERT(nvarchar(200), Name)) FROM (SELECT * FROM actual EXCEPT SELECT * FROM expected) AS u;';

IF EXISTS (SELECT * FROM @differences)
BEGIN
    SELECT @message = CONCAT(@message, N'; ', fact) FROM @differences ORDER BY fact;
    SET @message = LEFT(N'The brands or types differ from the reference data that InitialCreate seeds: ' + STUFF(@message, 1, 2, N''), 2048);
    THROW 50011, @message, 1;
END;

-- 7. Record InitialCreate as applied, in the table EF Core would have created.
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;

EXEC sp_executesql
    N'INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N''20260929065225_InitialCreate'', N''10.0.12'');';

COMMIT;
PRINT N'Baselined: InitialCreate is recorded as applied.';
