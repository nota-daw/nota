# Follow-up for the user manual (`nota-docs`, sibling repository)

The sibling repository `../nota-docs` (Astro Starlight, EN + RU) was not available when the
feature was built, so the manual is not updated yet. Add this to the Preferences → Appearance
page (EN) and its RU twin, re-shoot the Preferences screenshot with `NOTA_UI_LANGUAGE`, and
regenerate the shortcuts page only if a binding changed (none did).

## EN — Appearance → Language

**Language** sets the language of Nota's menus, panels, dialogs, tooltips and messages. Choose
**English** (the default) or **Français** from the list. The choice is saved with your other
Appearance settings and applies the next time Nota starts: the row says so, and **Restart**
closes Nota (it asks about unsaved changes first) and opens it again in the new language.

Device and parameter names ("Cutoff", "Attack", "Reverb") stay in English in every language,
and the names you give projects, tracks and clips are never translated. The language never
changes what is saved in a project. The Nota Remote phone page and the AI-control (MCP)
descriptions are not translated.

## RU — Внешний вид → Язык

**Язык** задаёт язык меню, панелей, диалогов, подсказок и сообщений Nota. Выберите
**English** (по умолчанию) или **Français** в списке. Выбор сохраняется вместе с остальными
настройками внешнего вида и применяется при следующем запуске: строка настройки сообщает об
этом, а кнопка **Перезапустить** закрывает Nota (сначала спросит о несохранённых изменениях)
и запускает её снова на новом языке.

Названия устройств и параметров («Cutoff», «Attack», «Reverb») остаются английскими на любом
языке, а имена проектов, дорожек и клипов, которые вы задаёте сами, никогда не переводятся.
Язык не влияет на то, что сохраняется в проекте. Веб-страница Nota Remote для телефона и
описания AI-управления (MCP) не переводятся.

> The RU text above is a draft by the implementer: have a Russian speaker check it, as with the
> French strings (`tasks.md` T074).
