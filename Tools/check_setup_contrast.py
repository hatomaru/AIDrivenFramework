"""Check the actual palette declared in the optional player setup stylesheet.

WCAG relative luminance: https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html
Includes hover, selected, disabled and focus text rather than relying on exceptions.
"""
from pathlib import Path
import re

FILE = Path(__file__).resolve().parents[1] / "src/AIDrivenFramework/Samples~/AISetup/Resources/PlayerSetup/Screen.uss"
CSS = FILE.read_text()


def color(selector, property_name):
    for selectors, body in re.findall(r"([^{}]+)\{([^{}]+)\}", CSS):
        selectors = re.sub(r"/\*.*?\*/", "", selectors, flags=re.S)
        if selector not in [x.strip() for x in selectors.split(",")]:
            continue
        match = re.search(r"(?:^|;)\s*" + re.escape(property_name) + r":\s*(#[0-9A-Fa-f]{6})", body)
        if match:
            return match.group(1)
    raise ValueError(f"Missing explicit {property_name} for {selector}")


def luminance(value):
    rgb = [int(value[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    linear = [v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in rgb]
    return sum(v * w for v, w in zip(linear, (0.2126, 0.7152, 0.0722)))


def check(label, foreground, background, minimum=4.5):
    a, b = sorted((luminance(foreground), luminance(background)))
    ratio = (b + 0.05) / (a + 0.05)
    print(f"{label}: {ratio:.2f}:1 ({foreground} / {background})")
    assert ratio >= minimum, label


for selector in (".screen", "Button", "Button:disabled", ".active-language", ".active-language:hover", "Button.primary", "Button.primary:hover", "Button.primary:disabled", "Button.link", "TextField .unity-base-field__input", ".unity-text-field__input"):
    check(selector, color(selector, "color"), color(selector, "background-color"))
white = color(".card", "background-color")
check("secondary text", color(".description", "color"), white)
check("notes", color(".note", "color"), white)
check("footer", color(".footer", "color"), color(".screen", "background-color"))
check("selected model", color(".model-tier", "color"), color(".model-card.selected", "background-color"))
check("selected model detail", color(".model-size", "color"), color(".model-card.selected", "background-color"))
check("details", color(".detail", "color"), color(".details", "background-color"))
check("link hover", color("Button.link", "color"), color("Button.link:hover", "background-color"))
check("error", color(".error", "color"), white)
check("progress fill", color(".unity-progress-bar__progress", "background-color"), color(".unity-progress-bar__background", "background-color"), 3)
check("focus border", color("Button:focus", "border-color"), white, 3)
check("scroll thumb", color(".unity-scroller--vertical .unity-base-slider__dragger", "background-color"), white, 3)
check("glass secondary (84% white over darkest possible backdrop)", color(".note", "color"), "#D6D6D6")
atmosphere = FILE.parents[2] / "SetupAtmosphere.cs"
source = atmosphere.read_text()
assert "Color.Lerp(blurred[i], Color.white, .84f)" in source, "Recheck the glass veil bound after changing its opacity"
endpoints = re.search(r"accent\[i\] = Color.Lerp\(new Color\((.*?)\), new Color\((.*?)\)", source).groups()
start, end = ([float(c.strip().rstrip("f")) for c in value.split(",")] for value in endpoints)
samples = ["#" + "".join(f"{round((a + (b - a) * i / 100) * 255):02X}" for a, b in zip(start, end)) for i in range(101)]
for name, background, minimum in (("accent / white text", "#FFFFFF", 4.5), ("accent / progress track", color(".unity-progress-bar__background", "background-color"), 3), ("icon / darkest glass", "#D6D6D6", 3)):
    worst = min(samples, key=lambda value: (max(luminance(value), luminance(background)) + .05) / (min(luminance(value), luminance(background)) + .05))
    check(name + " (101 samples)", worst, background, minimum)
print("PASS: all checked text >= 4.5:1; progress/focus >= 3:1.")
