<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# User manual handoff

The local EN/RU piano-roll pages were updated for the in-lane redesign. A paired
patch is included in `specs/083-inline-cc-controls/manual.patch` for submission to
the separate nota-docs repository; this PR only updates the existing app branch.
Validation status is recorded in that feature's validation report.

## English

Use **+ lane** beneath the left sidebar entries to add controller lanes. Each lane's
dropdown shows the controller name and CC number. The adjacent **×** hides the lane
and preserves its MIDI data. The **127 / 64 / 0** scale follows the graph on the left.
Extra lanes scroll vertically and share the notes' horizontal position
and zoom. Drag the divider above Velocity or a CC lane to resize it. Heights are
remembered when you reopen/restart and re-add the corresponding lane/controller.
The docked MIDI Clip panel stops shrinking while notes and all five lane tabs are
still usable. With the Mixer also docked, the window's minimum height grows to fit
both panels; hiding the Mixer releases that extra minimum.

## Русский

Кнопка **+ lane** под списком слева добавляет дорожки MIDI-контроллеров. Внутри каждой
дорожки есть список с именем контроллера и номером CC. Кнопка **×** рядом скрывает
дорожку, сохраняя MIDI-данные. Слева от графика расположена шкала **127 / 64 / 0**.
Дополнительные дорожки
прокручиваются по вертикали и используют горизонтальную позицию и масштаб нот.
Перетащите разделитель над Velocity или дорожкой CC, чтобы изменить её высоту.
Высота сохраняется после повторного открытия и перезапуска, когда вы снова добавляете
соответствующую дорожку с тем же контроллером.
При уменьшении закреплённой панели MIDI-клипа ноты и все пять вкладок остаются
доступными. Если микшер тоже закреплён, минимальная высота окна увеличивается,
чтобы вместить обе панели; скрытие микшера снимает это дополнительное ограничение.
