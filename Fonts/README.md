# Report fonts

Fonts the report designer offers and the server renders PNG/PDF exports with. The web UI loads the same
files from `GET /Reports/Fonts/{file}`, so text measures and looks the same in the browser and in exports.

All are Google Fonts under the SIL Open Font License 1.1 (see `licenses/`), taken from the
`@expo-google-fonts/*` npm packages and subset to Latin, Latin Extended, Vietnamese, Greek, Cyrillic and
common symbols. Characters outside that range fall back to system fonts.

To add a font: add the TTF files here and an entry to `fonts.json` (family, category, files with weight/italic).
