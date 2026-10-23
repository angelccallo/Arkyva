SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
-- Hash avoids the SQL Server index key byte limit for NVARCHAR(1000).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'arkyva.Files') AND name = N'UX_arkyva_Files_StorageIdentity')
    CREATE UNIQUE INDEX UX_arkyva_Files_StorageIdentity ON arkyva.Files(StorageIdentityHash);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'arkyva.Files') AND name = N'IX_arkyva_Files_Status_CreatedUtc_FileId')
    CREATE INDEX IX_arkyva_Files_Status_CreatedUtc_FileId ON arkyva.Files(Status, CreatedUtc, FileId)
        INCLUDE (OriginalName, Extension);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'arkyva.Files') AND name = N'IX_arkyva_Files_CreatedUtc_FileId')
    CREATE INDEX IX_arkyva_Files_CreatedUtc_FileId ON arkyva.Files(CreatedUtc, FileId);
GO
