SELECT g.game_id AS GameId, g.name AS Name, g.provider_id AS ProviderId, g.category_key AS Category, g.position AS Position, g.image_url AS ImageUrl,
       g.demo_available AS DemoAvailable, COALESCE(m.enabled, false) AS Enabled, g.synced_at AS SyncedAt
FROM catalog.games g
LEFT JOIN catalog.game_markets m ON m.game_id = g.game_id AND m.market = @Market
WHERE (@ProviderId::text IS NULL OR g.provider_id = @ProviderId)
ORDER BY g.category_key, g.position, g.name;
