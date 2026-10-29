/*
   模块：结构冒烟测试。
   作用：检查表、视图、过滤唯一索引和关键字段是否按设计创建。
   结果：本文件只读，不修改业务数据。
*/
USE [PortQueue];
GO

SET NOCOUNT ON;

SELECT 'tables' AS check_name, COUNT(*) AS actual_value
FROM sys.tables
WHERE schema_id = SCHEMA_ID('dbo');

SELECT 'views' AS check_name, COUNT(*) AS actual_value
FROM sys.views
WHERE schema_id = SCHEMA_ID('dbo')
  AND name IN ('v_port_event', 'v_anchorage_queue', 'v_current_berth_plan');

SELECT 'filtered_unique_indexes' AS check_name, COUNT(*) AS actual_value
FROM sys.indexes
WHERE object_id IN (OBJECT_ID('dbo.anchorage_queue_entry'), OBJECT_ID('dbo.berth_plan_revision'))
  AND filter_definition IS NOT NULL;

SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME IN ('ship', 'berth_ship_type', 'dictionary_item')
ORDER BY TABLE_NAME, ORDINAL_POSITION;

SELECT TOP (20) * FROM dbo.v_anchorage_queue ORDER BY created_at DESC;
SELECT TOP (20) * FROM dbo.v_current_berth_plan ORDER BY planned_berthing_at;
