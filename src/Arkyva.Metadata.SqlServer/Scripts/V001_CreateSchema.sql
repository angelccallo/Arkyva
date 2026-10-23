-- Run explicitly in the target database. No automatic migrations.
IF SCHEMA_ID(N'arkyva') IS NULL
    EXEC(N'CREATE SCHEMA arkyva AUTHORIZATION dbo;');
GO
