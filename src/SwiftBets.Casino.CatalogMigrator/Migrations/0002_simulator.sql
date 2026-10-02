CREATE SCHEMA IF NOT EXISTS simulator;

-- The simulated providers' own record of every wallet call they made, which their daily reports are built from.
CREATE TABLE simulator.transactions
(
    provider_id             text        NOT NULL,
    provider_transaction_id text        NOT NULL,
    kind                    text        NOT NULL,
    amount                  bigint      NOT NULL CHECK (amount >= 0),
    currency                char(3)     NOT NULL,
    round_id                text        NOT NULL,
    game_id                 text        NOT NULL,
    sent_at                 timestamptz NOT NULL,
    PRIMARY KEY (provider_id, provider_transaction_id)
);

CREATE INDEX ix_simulator_transactions_day ON simulator.transactions (provider_id, sent_at);
