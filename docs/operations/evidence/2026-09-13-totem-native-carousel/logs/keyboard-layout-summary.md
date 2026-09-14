# Keyboard and layout evidence summary

This durable summary retains the settled observations from the original local
browser run against candidate code SHA
`c76c2be20edf5d3d6b122b76a9ba9c38de92c28a`. It intentionally does **not**
retain the original raw `layout-checks.log`: one final 390×844 `Home` sample
reported Bruno during a transition (`centerError: -28.1`, `matrix3d(...)`), so
it was not settled evidence of the completed Home action.

The durable reproduction command is
[`../scripts/check-layout.ps1`](../scripts/check-layout.ps1). It fixes that
wait condition: after every key it requires the expected professional identity,
an active-card center error below 4px, and the settled active-card `matrix(...)`
transform. It has been preserved for future reproduction and is not presented
as a replacement execution of the original candidate run.

## Settled keyboard output retained from the original run

At 390×844, the retained settled records were:

| Key | Active professional | Center error | Focus |
| --- | --- | --- | --- |
| Home | Ana Souza | -0.01px | `Profissionais`, visible |
| End | Elisa Martins | -0.45px | `Profissionais`, visible |
| ArrowLeft | Diego Santos | 0.16px | `Profissionais`, visible |
| ArrowRight | Elisa Martins | -0.45px | `Profissionais`, visible |

The original run also recorded settled Home/End/Arrow results at 1920×1080,
1366×768, 768×1024, and 320×568. The four required viewport geometry records
are retained verbatim in [final-metrics.log](final-metrics.log).

## Short mobile and reduced-motion output

At 390×600 before a vertical wheel over the card, the document was 753px high
and `scrollY` was 0. After the wheel, `scrollY` was 153. The corresponding
capture is [after-390x600-scrolled.png](../screenshots/after-390x600-scrolled.png).

At 390×844 with `prefers-reduced-motion: reduce`, an End action settled on
Elisa Martins with `scrollBehavior: "auto"`, `activeTransform: "none"`,
visible focus, and center error -0.45px. The corresponding capture is
[after-390x844-reduced-motion.png](../screenshots/after-390x844-reduced-motion.png).

These are local browser-automation results, not physical touch evidence.
