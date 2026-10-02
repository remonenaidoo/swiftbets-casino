SELECT g.game_id AS GameId, g.name AS Name, g.provider_id AS ProviderId, p.wallet_model AS WalletModel, g.min_bet AS MinBet
FROM catalog.games g JOIN catalog.providers p ON p.provider_id = g.provider_id
WHERE g.game_id = @GameId;
