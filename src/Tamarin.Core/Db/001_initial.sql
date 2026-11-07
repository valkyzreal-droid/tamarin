-- Quick-capture notepad: SQLite schema, migration 001.
-- Single user, local file, so there is no user_id or row level security.
-- Access rule: the database file lives in the user's own %APPDATA% folder
-- (or next to the .exe in portable mode) and only this app opens it.
--
-- Per-connection settings, run on every open (they are not stored in the file):
--   PRAGMA foreign_keys = ON;      -- SQLite ignores foreign keys unless asked
--   PRAGMA journal_mode = WAL;     -- readers never block the autosave writer
--   PRAGMA synchronous = FULL;     -- a few tiny writes; losing text is worse than speed
--   PRAGMA secure_delete = ON;     -- deleted note text is overwritten, not left in free pages
--   PRAGMA busy_timeout = 3000;
--
-- Times: INTEGER unix epoch milliseconds, always UTC. Convert to local time only in the UI.
-- Millisecond precision comes from julianday('now'); strftime('%s') only has 1 second, which
-- left notes created in the same second in random order.
-- IDs: 32 hex characters from randomblob(16), generated in SQL so inserts need no id.

BEGIN;

CREATE TABLE notes (
  id          TEXT    PRIMARY KEY NOT NULL DEFAULT (lower(hex(randomblob(16)))),
  body        TEXT    NOT NULL DEFAULT '',
  kind        TEXT    NOT NULL DEFAULT 'text'
              CHECK (kind IN ('text', 'checklist', 'code', 'math')),
  pinned      INTEGER NOT NULL DEFAULT 0 CHECK (pinned IN (0, 1)),
  created_at  INTEGER NOT NULL DEFAULT (CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)),
  updated_at  INTEGER NOT NULL DEFAULT (CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)),
  expires_at  INTEGER,                       -- null = never expires
  deleted_at  INTEGER,                       -- null = alive, set = in the trash
  CHECK (length(body) <= 2000000),           -- 2 MB cap keeps search and autosave fast
  CHECK (expires_at IS NULL OR expires_at > created_at)
);

-- The list in S02 and the "next / previous note" keys: alive notes, pinned first, newest first.
CREATE INDEX notes_list_idx    ON notes (deleted_at, pinned DESC, updated_at DESC);
-- The trash screen (S04) and the purge job.
CREATE INDEX notes_trash_idx   ON notes (deleted_at) WHERE deleted_at IS NOT NULL;
-- The expiry sweep only looks at notes that can expire.
CREATE INDEX notes_expiry_idx  ON notes (expires_at) WHERE expires_at IS NOT NULL;

-- Keep updated_at honest even if a caller forgets. Recursive triggers are off by
-- default in SQLite, so this does not loop.
CREATE TRIGGER notes_touch AFTER UPDATE OF body, kind, pinned ON notes
BEGIN
  UPDATE notes
     SET updated_at = CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)
   WHERE id = NEW.id;
END;

-- Full-text search. External-content table: the text is stored once, in notes.
-- unicode61 with remove_diacritics 2 means "acao" finds "ação" and "cafe" finds "café".
CREATE VIRTUAL TABLE notes_fts USING fts5(
  body,
  content = 'notes',
  content_rowid = 'rowid',
  tokenize = 'unicode61 remove_diacritics 2'
);

CREATE TRIGGER notes_fts_ai AFTER INSERT ON notes BEGIN
  INSERT INTO notes_fts (rowid, body) VALUES (NEW.rowid, NEW.body);
END;

CREATE TRIGGER notes_fts_ad AFTER DELETE ON notes BEGIN
  INSERT INTO notes_fts (notes_fts, rowid, body) VALUES ('delete', OLD.rowid, OLD.body);
END;

CREATE TRIGGER notes_fts_au AFTER UPDATE OF body ON notes BEGIN
  INSERT INTO notes_fts (notes_fts, rowid, body) VALUES ('delete', OLD.rowid, OLD.body);
  INSERT INTO notes_fts (rowid, body) VALUES (NEW.rowid, NEW.body);
END;

-- Could-have: images pasted into a note, with text extracted by Windows OCR.
CREATE TABLE attachments (
  id          TEXT    PRIMARY KEY NOT NULL DEFAULT (lower(hex(randomblob(16)))),
  note_id     TEXT    NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
  kind        TEXT    NOT NULL DEFAULT 'image' CHECK (kind IN ('image')),
  path        TEXT    NOT NULL,              -- relative to the app data folder
  ocr_text    TEXT,
  created_at  INTEGER NOT NULL DEFAULT (CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER))
);
CREATE INDEX attachments_note_idx ON attachments (note_id);

-- Could-have: which window a captured note came from. One row at most per note.
CREATE TABLE source_apps (
  note_id       TEXT PRIMARY KEY NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
  process_name  TEXT NOT NULL,
  window_title  TEXT
);

-- App settings as key/value text. The app validates values; the database only stores them.
CREATE TABLE settings (
  key         TEXT    PRIMARY KEY NOT NULL,
  value       TEXT    NOT NULL,
  updated_at  INTEGER NOT NULL DEFAULT (CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER))
);

INSERT INTO settings (key, value) VALUES
  ('hotkey',               'Ctrl+Shift+Space'),  -- no Ctrl+Alt combos: on layouts with AltGr (ABNT2) that is how accented and special characters are typed
  ('start_with_windows',   '0'),
  ('theme',                'system'),            -- system | light | dark
  ('text_size',            '14'),
  ('window_pinned',        '0'),
  ('trash_retention_days', '30'),
  ('backup_enabled',       '1'),
  ('backup_keep',          '7'),
  ('network_features',     '0');                 -- live currency rates etc. stay off until the user opts in

PRAGMA user_version = 1;

COMMIT;
