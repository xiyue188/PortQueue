/*
   模块：数据库级完整性保护。
   作用：保护字典船型引用、事件分类匹配和不可重复的执行事实。
   原则：触发器只承担跨表且必须由数据库兜底的约束，不替代业务过程。
*/
USE [PortQueue];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER TRIGGER dbo.trg_ship_type_reference
ON dbo.ship
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        LEFT JOIN deleted AS old_row ON old_row.ship_id = i.ship_id
        LEFT JOIN dbo.dictionary_item AS d ON d.dictionary_item_id = i.ship_type_item_id
        WHERE d.dictionary_item_id IS NULL
           OR d.dictionary_type <> 'SHIP_TYPE'
           OR (d.is_enabled = 0 AND (old_row.ship_id IS NULL OR old_row.ship_type_item_id <> i.ship_type_item_id))
    )
    BEGIN
        THROW 51001, 'Ship type must reference an enabled SHIP_TYPE dictionary item.', 1;
    END;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_berth_ship_type_reference
ON dbo.berth_ship_type
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        LEFT JOIN deleted AS old_row
            ON old_row.berth_id = i.berth_id
           AND old_row.ship_type_item_id = i.ship_type_item_id
        LEFT JOIN dbo.dictionary_item AS d ON d.dictionary_item_id = i.ship_type_item_id
        WHERE d.dictionary_item_id IS NULL
           OR d.dictionary_type <> 'SHIP_TYPE'
           OR (d.is_enabled = 0 AND old_row.berth_id IS NULL)
    )
    BEGIN
        THROW 51002, 'Berth ship type must reference an enabled SHIP_TYPE dictionary item.', 1;
    END;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_anchorage_event_link_guard
ON dbo.anchorage_event_link
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.port_event AS e ON e.event_id = i.event_id
        WHERE e.event_type_code NOT IN ('ANCHORAGE_ENTER', 'ANCHORAGE_LEAVE')
    )
    BEGIN
        THROW 51003, 'Anchorage links require an anchorage enter or leave event.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.port_entry_event_link AS p ON p.event_id = i.event_id
    )
    OR EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.berth_event_link AS b ON b.event_id = i.event_id
    )
    BEGIN
        THROW 51004, 'An event can have only one category link.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.port_event AS e ON e.event_id = i.event_id
        INNER JOIN dbo.anchorage_event_link AS existing
            ON existing.queue_entry_id = i.queue_entry_id
        INNER JOIN dbo.port_event AS old_event ON old_event.event_id = existing.event_id
        WHERE e.event_type_code = 'ANCHORAGE_ENTER'
          AND existing.event_id <> i.event_id
          AND old_event.event_type_code = 'ANCHORAGE_ENTER'
    )
    BEGIN
        THROW 51009, 'A queue stay can have only one anchorage enter event.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.port_event AS e ON e.event_id = i.event_id
        INNER JOIN dbo.anchorage_event_link AS existing
            ON existing.queue_entry_id = i.queue_entry_id
        INNER JOIN dbo.port_event AS old_event ON old_event.event_id = existing.event_id
        WHERE e.event_type_code = 'ANCHORAGE_LEAVE'
          AND existing.event_id <> i.event_id
          AND old_event.event_type_code = 'ANCHORAGE_LEAVE'
    )
    BEGIN
        THROW 51010, 'A queue stay can have only one anchorage leave event.', 1;
    END;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_port_event_type_immutable
ON dbo.port_event
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE(event_type_code)
       AND EXISTS
       (
           SELECT 1
           FROM inserted AS i
           INNER JOIN deleted AS d ON d.event_id = i.event_id
           WHERE i.event_type_code <> d.event_type_code
       )
    BEGIN
        THROW 51011, 'Event type is immutable after creation.', 1;
    END;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_port_entry_event_link_guard
ON dbo.port_entry_event_link
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.port_event AS e ON e.event_id = i.event_id
        WHERE e.event_type_code <> 'PORT_ENTER'
    )
    BEGIN
        THROW 51005, 'Port entry links require a PORT_ENTER event.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.anchorage_event_link AS a ON a.event_id = i.event_id
    )
    OR EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.berth_event_link AS b ON b.event_id = i.event_id
    )
    BEGIN
        THROW 51006, 'An event can have only one category link.', 1;
    END;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_berth_event_link_guard
ON dbo.berth_event_link
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.port_event AS e ON e.event_id = i.event_id
        WHERE e.event_type_code NOT IN ('BERTHING', 'UNBERTHING')
    )
    BEGIN
        THROW 51007, 'Berth links require a berthing or unberthing event.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.anchorage_event_link AS a ON a.event_id = i.event_id
    )
    OR EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.port_entry_event_link AS p ON p.event_id = i.event_id
    )
    BEGIN
        THROW 51008, 'An event can have only one category link.', 1;
    END;
END;
GO
