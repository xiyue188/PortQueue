/*
   模块：结构冒烟测试。
   作用：检查表、视图、过滤唯一索引和关键字段是否按设计创建。
   结果：本文件只读，不修改业务数据。
*/
USE [PortQueue];
GO

SET NOCOUNT ON;

-- 数量与约束状态是验收条件，不只打印一个看似正常的清单。
IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND is_ms_shipped=0)<>21
    THROW 53020,'Expected 21 business tables.',1;
IF (SELECT COUNT(*) FROM sys.views WHERE schema_id=SCHEMA_ID('dbo') AND is_ms_shipped=0)<>3
    THROW 53021,'Expected 3 business views.',1;
IF (SELECT COUNT(*) FROM sys.procedures WHERE schema_id=SCHEMA_ID('dbo') AND is_ms_shipped=0)<>7
    THROW 53022,'Expected 7 business procedures.',1;
IF (SELECT COUNT(*) FROM sys.triggers WHERE parent_class=1 AND is_ms_shipped=0)<>8
    THROW 53023,'Expected 8 table triggers.',1;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1)
    OR EXISTS(SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1)
    OR EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=1)
    THROW 53024,'Disabled or untrusted database protection.',1;

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

SELECT object_type, object_count
FROM
(
    SELECT 'tables' AS object_type, COUNT(*) AS object_count
    FROM sys.tables
    WHERE schema_id = SCHEMA_ID('dbo') AND is_ms_shipped = 0
    UNION ALL
    SELECT 'views', COUNT(*)
    FROM sys.views
    WHERE schema_id = SCHEMA_ID('dbo')
      AND name IN ('v_port_event', 'v_anchorage_queue', 'v_current_berth_plan')
    UNION ALL
    SELECT 'procedures', COUNT(*)
    FROM sys.procedures
    WHERE schema_id = SCHEMA_ID('dbo') AND is_ms_shipped = 0
    UNION ALL
    SELECT 'triggers', COUNT(*)
    FROM sys.triggers
    WHERE parent_class = 1 AND is_ms_shipped = 0
    UNION ALL
    SELECT 'indexes', COUNT(*)
    FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id
    WHERE t.schema_id=SCHEMA_ID('dbo') AND t.is_ms_shipped=0 AND i.index_id>0
) AS object_summary
ORDER BY object_type;

SELECT 'reference_data' AS check_name, reference_table, row_count
FROM
(
    SELECT 'role' AS reference_table, COUNT(*) AS row_count FROM dbo.role
    UNION ALL SELECT 'dictionary_item', COUNT(*) FROM dbo.dictionary_item
    UNION ALL SELECT 'port', COUNT(*) FROM dbo.port
    UNION ALL SELECT 'terminal', COUNT(*) FROM dbo.terminal
    UNION ALL SELECT 'anchorage', COUNT(*) FROM dbo.anchorage
    UNION ALL SELECT 'berth', COUNT(*) FROM dbo.berth
    UNION ALL SELECT 'berth_ship_type', COUNT(*) FROM dbo.berth_ship_type
) AS reference_summary
ORDER BY reference_table;

SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME IN ('ship', 'berth_ship_type', 'dictionary_item')
ORDER BY TABLE_NAME, ORDINAL_POSITION;

SELECT TOP (20) * FROM dbo.v_anchorage_queue ORDER BY created_at DESC;
SELECT TOP (20) * FROM dbo.v_current_berth_plan ORDER BY planned_berthing_at;
