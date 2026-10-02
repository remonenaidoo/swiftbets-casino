SELECT g.game_id AS GameId, g.name AS Name, g.provider_id AS ProviderId, g.category_key AS Category, g.tag AS Tag, g.min_bet AS MinBet, g.currency AS Currency,
       p.wallet_model AS WalletModel,
       EXISTS (SELECT 1 FROM catalog.game_markets m WHERE m.game_id = g.game_id AND m.market = @Market AND m.enabled) AS Available
FROM catalog.games g
JOIN catalog.providers p ON p.provider_id = g.provider_id
WHERE g.game_id = @GameId;
