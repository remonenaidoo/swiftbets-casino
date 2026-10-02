SELECT c.category_key AS CategoryKey, c.name AS CategoryName, g.game_id AS GameId, g.name AS Name, g.provider_id AS ProviderId,
       g.tag AS Tag, g.min_bet AS MinBet, g.currency AS Currency
FROM catalog.games g
JOIN catalog.categories c ON c.category_key = g.category_key
WHERE EXISTS (SELECT 1 FROM catalog.game_markets m WHERE m.game_id = g.game_id AND m.market = @Market AND m.enabled)
ORDER BY c.position, g.position;
