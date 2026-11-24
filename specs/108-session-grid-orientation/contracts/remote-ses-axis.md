# Contract: `ses` message — Session grid orientation (spec 108)

Nota → phone / Switch, the existing `ses` message (specs/014, 102, 107) gains one optional field:

```json
{ "t": "ses", "n": 8, "ax": "across", "sel": null, "q": [], "tr": [ ... ] }
```

| Field | Type | Meaning |
|---|---|---|
| `ax` | `"across"` or absent | The desktop's Session grid preference. Present only when it is Scenes across; absent means Scenes down. |

- Sent with every `ses` build; because `ses` is resent whenever its text changes, a switch on
  the desktop reaches every connected client on the next state tick, with no new message type.
- Older clients ignore the field. Clients talking to an older Nota never see it and treat
  "As in Nota" as Scenes down.
- No client → Nota message is added: a device's own choice stays on the device.
