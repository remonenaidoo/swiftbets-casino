SELECT f.game_id FROM catalog.favourites f WHERE f.punter_id = @PunterId ORDER BY f.created_at DESC;
