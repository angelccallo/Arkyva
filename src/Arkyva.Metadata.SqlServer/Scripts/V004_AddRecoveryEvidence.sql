SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
-- Additive migration. Deploy with all writers stopped; do not mix old/new writer binaries.
-- Never automatically applied by constructors or recovery.
IF COL_LENGTH(N'arkyva.Files', N'Revision') IS NULL
    ALTER TABLE arkyva.Files ADD Revision BIGINT NOT NULL
        CONSTRAINT DF_arkyva_Files_Revision DEFAULT (0) WITH VALUES;
GO
IF COL_LENGTH(N'arkyva.Files', N'CompletionSha256') IS NULL
    ALTER TABLE arkyva.Files ADD CompletionSha256 CHAR(64) COLLATE Latin1_General_100_BIN2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'arkyva.Files') AND name = N'CK_arkyva_Files_Revision')
    ALTER TABLE arkyva.Files ADD CONSTRAINT CK_arkyva_Files_Revision CHECK (Revision >= 0);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'arkyva.Files') AND name = N'CK_arkyva_Files_Receipt')
    ALTER TABLE arkyva.Files ADD CONSTRAINT CK_arkyva_Files_Receipt CHECK
        (CompletionSha256 IS NULL OR (DATALENGTH(CompletionSha256) = 64 AND CompletionSha256 NOT LIKE '%[^0-9a-f]%'));
GO
