/* M2 additive schema. No USE: caller must explicitly select isolated mobile database. */
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF DB_NAME() <> N'PortQueueMobileDev' AND DB_NAME() NOT LIKE N'PortQueueStage2Test[_]%'
    THROW 52500, 'Mobile schema requires isolated mobile/test database.', 1;
GO
IF OBJECT_ID(N'dbo.mobile_report',N'U') IS NULL
CREATE TABLE dbo.mobile_report (
 report_id bigint IDENTITY PRIMARY KEY,
 request_id uniqueidentifier NOT NULL,
 port_call_id bigint NOT NULL REFERENCES dbo.port_call(port_call_id),
 report_type varchar(10) NOT NULL CHECK(report_type IN('PHOTO','ETA')),
 category varchar(10) NULL,
 description nvarchar(500) NOT NULL CHECK(LEN(LTRIM(RTRIM(description))) BETWEEN 1 AND 500),
 proposed_eta datetime2(7) NULL,
 status_code varchar(12) NOT NULL DEFAULT 'SUBMITTED' CHECK(status_code IN('SUBMITTED','ACCEPTED','REJECTED')),
 submitted_by bigint NOT NULL REFERENCES dbo.user_account(user_id),
 submitted_at datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
 processed_by bigint NULL REFERENCES dbo.user_account(user_id),
 processed_at datetime2(7) NULL,
 process_comment nvarchar(500) NULL,
 payload_hash binary(32) NOT NULL,
 row_version rowversion NOT NULL,
 CONSTRAINT UQ_mobile_report_request UNIQUE(submitted_by,request_id),
 CONSTRAINT CK_mobile_report_type CHECK((report_type='PHOTO' AND category IN('SITE','ARRIVAL','DELAY') AND category IS NOT NULL AND proposed_eta IS NULL) OR (report_type='ETA' AND category IS NULL AND proposed_eta IS NOT NULL)),
 CONSTRAINT CK_mobile_report_processing CHECK((status_code='SUBMITTED' AND processed_by IS NULL AND processed_at IS NULL AND process_comment IS NULL) OR (status_code IN('ACCEPTED','REJECTED') AND processed_by IS NOT NULL AND processed_at IS NOT NULL AND processed_at>=submitted_at AND process_comment IS NOT NULL AND LEN(LTRIM(RTRIM(process_comment))) BETWEEN 1 AND 500))
);
GO
IF OBJECT_ID(N'dbo.report_photo',N'U') IS NULL
CREATE TABLE dbo.report_photo (
 photo_id bigint IDENTITY PRIMARY KEY,
 report_id bigint NOT NULL UNIQUE REFERENCES dbo.mobile_report(report_id),
 storage_key varchar(40) NOT NULL UNIQUE CHECK(storage_key NOT LIKE '%[^a-z0-9.]%' AND LEN(storage_key)=36 AND RIGHT(storage_key,4)='.jpg'),
 mime_type varchar(20) NOT NULL CHECK(mime_type='image/jpeg'),
 byte_size bigint NOT NULL CHECK(byte_size BETWEEN 1 AND 5242880),
 captured_at datetime2(7) NOT NULL,
 latitude decimal(10,7) NULL,
 longitude decimal(10,7) NULL,
 accuracy_m float NULL,
 location_at datetime2(7) NULL,
 coordinate_system varchar(10) NULL,
 CONSTRAINT CK_report_photo_location CHECK((latitude IS NULL AND longitude IS NULL AND accuracy_m IS NULL AND location_at IS NULL AND coordinate_system IS NULL) OR (latitude IS NOT NULL AND longitude IS NOT NULL AND accuracy_m IS NOT NULL AND location_at IS NOT NULL AND coordinate_system IS NOT NULL AND latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180 AND accuracy_m>=0 AND coordinate_system='WGS84'))
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.mobile_report') AND name='IX_mobile_report_inbox')
 CREATE INDEX IX_mobile_report_inbox ON dbo.mobile_report(status_code,submitted_at DESC,report_id DESC) INCLUDE(submitted_by,port_call_id);
GO
