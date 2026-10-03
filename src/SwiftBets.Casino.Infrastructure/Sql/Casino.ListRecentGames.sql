SELECT TOP (@Limit) ProviderId, GameId, MAX(CreatedAt) AS PlayedAt
FROM casino.Sessions
WHERE PunterId = @PunterId
GROUP BY ProviderId, GameId
ORDER BY MAX(CreatedAt) DESC;
