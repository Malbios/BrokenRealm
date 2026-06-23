CREATE TABLE IF NOT EXISTS world_events (
  id uuid PRIMARY KEY,
  occurred_at timestamptz NOT NULL DEFAULT now(),
  actor_id text NULL,
  event_type text NOT NULL,
  script_module text NULL,
  script_version int NULL,
  data jsonb NOT NULL
);

CREATE TABLE IF NOT EXISTS localized_messages (
  key text NOT NULL,
  culture text NOT NULL,
  text text NOT NULL,
  PRIMARY KEY (key, culture)
);

INSERT INTO localized_messages(key, culture, text)
VALUES
  ('look.forest', 'en', 'You are standing in a quiet forest.'),
  ('look.forest', 'de', 'Du stehst in einem stillen Wald.'),
  ('gather.wood.success', 'en', 'You gather {amount} wood.'),
  ('gather.wood.success', 'de', 'Du sammelst {amount} Holz.')
ON CONFLICT DO NOTHING;