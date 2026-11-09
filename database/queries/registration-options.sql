/* 实际启用资料候选，关键词按字面查找，分页不截掉其余选择。 */
WITH candidates AS (
 SELECT CONVERT(varchar(20),s.ship_id) Id,s.ship_name Name,CONCAT('IMO ',s.imo_no,' / MMSI ',s.mmsi) Detail,s.max_draft_m MaxDraftM
 FROM dbo.ship s JOIN dbo.dictionary_item d ON d.dictionary_item_id=s.ship_type_item_id
 WHERE @Kind='ships' AND s.status_code='ACTIVE' AND d.dictionary_type='SHIP_TYPE' AND d.is_enabled=1
 UNION ALL
 SELECT CONVERT(varchar(20),port_id),port_name,port_code,NULL FROM dbo.port WHERE @Kind='ports' AND status_code='ENABLED'
), filtered AS (SELECT * FROM candidates WHERE @Keyword=N'' OR CHARINDEX(@Keyword,Name)>0 OR CHARINDEX(@Keyword,Detail)>0)
SELECT (SELECT COUNT_BIG(*) FROM filtered) TotalCount,
 COALESCE((SELECT * FROM filtered ORDER BY Name,CONVERT(bigint,Id) OFFSET @Offset ROWS FETCH NEXT 20 ROWS ONLY FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]') ItemsJson;
