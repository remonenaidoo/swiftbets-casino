CREATE SCHEMA IF NOT EXISTS catalog;

CREATE TABLE catalog.providers
(
    provider_id  text PRIMARY KEY,
    name         text NOT NULL,
    wallet_model text NOT NULL CHECK (wallet_model IN ('seamless', 'transfer'))
);

CREATE TABLE catalog.categories
(
    category_key text PRIMARY KEY,
    name         text NOT NULL,
    position     int  NOT NULL
);

CREATE TABLE catalog.games
(
    game_id      text   PRIMARY KEY,
    name         text   NOT NULL,
    category_key text   NOT NULL REFERENCES catalog.categories (category_key),
    provider_id  text   NOT NULL REFERENCES catalog.providers (provider_id),
    tag          text   NULL CHECK (tag IN ('NEW', 'EXCLUSIVE')),
    min_bet      bigint NOT NULL CHECK (min_bet > 0),
    currency     char(3) NOT NULL,
    position     int    NOT NULL
);

-- Where a game may be offered; a game with no enabled market is hidden from the lobby.
CREATE TABLE catalog.game_markets
(
    game_id text    NOT NULL REFERENCES catalog.games (game_id),
    market  char(2) NOT NULL,
    enabled boolean NOT NULL DEFAULT true,
    PRIMARY KEY (game_id, market)
);

INSERT INTO catalog.providers (provider_id, name, wallet_model) VALUES
    ('sim-seamless', 'Swift Studios', 'seamless'),
    ('sim-transfer', 'Transfer Games', 'transfer');

INSERT INTO catalog.categories (category_key, name, position) VALUES
    ('slots', 'Slots', 1),
    ('live', 'Live casino', 2),
    ('crash', 'Crash', 3);

INSERT INTO catalog.games (game_id, name, category_key, provider_id, tag, min_bet, currency, position) VALUES
    ('sun-temple', 'Sun Temple', 'slots', 'sim-seamless', 'EXCLUSIVE', 100, 'ZAR', 1),
    ('deep-blue', 'Deep Blue', 'slots', 'sim-seamless', 'NEW', 100, 'ZAR', 2),
    ('gold-rush', 'Gold Rush 500', 'slots', 'sim-seamless', NULL, 50, 'ZAR', 3),
    ('rose-nights', 'Rose Nights', 'slots', 'sim-seamless', NULL, 100, 'ZAR', 4),
    ('jungle-kong', 'Jungle Kong', 'slots', 'sim-seamless', 'NEW', 100, 'ZAR', 5),
    ('lucky-lion', 'Lucky Lion', 'slots', 'sim-seamless', NULL, 100, 'ZAR', 6),
    ('neon-sevens', 'Neon Sevens', 'slots', 'sim-seamless', 'NEW', 100, 'ZAR', 7),
    ('lightning-roulette', 'Lightning Roulette', 'live', 'sim-transfer', NULL, 200, 'ZAR', 1),
    ('vip-blackjack', 'VIP Blackjack', 'live', 'sim-transfer', NULL, 2000, 'ZAR', 2),
    ('crazy-wheel', 'Crazy Wheel', 'live', 'sim-transfer', NULL, 200, 'ZAR', 3),
    ('jet-rush', 'Jet Rush', 'crash', 'sim-seamless', 'EXCLUSIVE', 100, 'ZAR', 1),
    ('rocket', 'Rocket', 'crash', 'sim-seamless', NULL, 100, 'ZAR', 2);

INSERT INTO catalog.game_markets (game_id, market) SELECT game_id, 'ZA' FROM catalog.games;
