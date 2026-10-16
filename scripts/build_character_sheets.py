import os
import re
import yaml

# High-fidelity HTML Template optimized for high information density
# Contains stats cards, action character assets, and full 8-ability layouts!
HTML_TEMPLATE = """<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <title>{name} - SLOPArena Character Sheet</title>
    <style>
        @import url('https://fonts.googleapis.com/css2?family=Orbitron:wght@400;700;900&family=Rajdhani:wght@400;500;600;700&display=swap');
        
        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}
        
        body {{
            background-color: #0b0c10;
            color: #c5c6c7;
            font-family: 'Rajdhani', sans-serif;
            width: 1200px;
            height: 1200px;
            overflow: hidden;
            display: flex;
            flex-direction: column;
            border: 8px solid #1f2833;
            position: relative;
        }}

        body::before {{
            content: '';
            position: absolute;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: 
                radial-gradient(circle at 10% 10%, rgba(216, 67, 21, 0.08) 0%, transparent 40%),
                radial-gradient(circle at 90% 90%, rgba(21, 101, 192, 0.08) 0%, transparent 40%),
                linear-gradient(rgba(255,255,255,0.01) 1px, transparent 1px),
                linear-gradient(90deg, rgba(255,255,255,0.01) 1px, transparent 1px);
            background-size: 100% 100%, 100% 100%, 20px 20px, 20px 20px;
            z-index: 1;
            pointer-events: none;
        }}

        /* Header Style */
        .header {{
            background: linear-gradient(135deg, #1f2833 0%, #0b0c10 100%);
            padding: 20px 30px;
            border-bottom: 3px solid #66fcf1;
            display: flex;
            justify-content: space-between;
            align-items: center;
            z-index: 5;
            position: relative;
        }}

        .header-title-area {{
            display: flex;
            flex-direction: column;
        }}

        .header-game {{
            font-family: 'Orbitron', sans-serif;
            font-size: 13px;
            font-weight: 900;
            color: #66fcf1;
            text-transform: uppercase;
            letter-spacing: 4px;
            margin-bottom: 3px;
            text-shadow: 0 0 10px rgba(102, 252, 241, 0.4);
        }}

        .header-name {{
            font-family: 'Orbitron', sans-serif;
            font-size: 42px;
            font-weight: 900;
            color: #ffffff;
            text-transform: uppercase;
            letter-spacing: 2px;
            line-height: 1;
        }}

        .header-tagline {{
            font-size: 16px;
            color: #45f3ff;
            font-weight: 500;
            margin-top: 3px;
            letter-spacing: 1px;
            text-transform: uppercase;
        }}

        .status-badge {{
            font-family: 'Orbitron', sans-serif;
            background-color: rgba(102, 252, 241, 0.1);
            border: 1px solid #66fcf1;
            color: #66fcf1;
            padding: 6px 12px;
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 1.5px;
            border-radius: 4px;
            box-shadow: 0 0 15px rgba(102, 252, 241, 0.1);
        }}

        /* Main Content Grid */
        .main-container {{
            flex: 1;
            display: grid;
            grid-template-columns: 460px 1fr;
            z-index: 5;
            position: relative;
            background: rgba(11, 12, 16, 0.85);
        }}

        /* Left Column: Visual, Palette, Concept */
        .left-column {{
            border-right: 2px solid #1f2833;
            display: flex;
            flex-direction: column;
            padding: 20px;
            justify-content: flex-start;
            position: relative;
            gap: 15px;
        }}

        .character-preview-card {{
            border: 2px solid #1f2833;
            background: rgba(31, 40, 51, 0.4);
            border-radius: 8px;
            overflow: hidden;
            height: 380px;
            display: flex;
            justify-content: center;
            align-items: center;
            position: relative;
            box-shadow: inset 0 0 30px rgba(0,0,0,0.6);
        }}

        .character-image {{
            max-width: 95%;
            max-height: 95%;
            object-fit: contain;
            filter: drop-shadow(0 0 20px rgba(0, 0, 0, 0.8));
            z-index: 2;
        }}

        .preview-overlay-corners {{
            position: absolute;
            top: 10px;
            left: 10px;
            right: 10px;
            bottom: 10px;
            pointer-events: none;
        }}
        .preview-overlay-corners::before,
        .preview-overlay-corners::after {{
            content: '';
            position: absolute;
            width: 15px;
            height: 15px;
            border-color: #66fcf1;
            border-style: solid;
        }}
        .preview-overlay-corners::before {{
            top: 0; left: 0; border-width: 2px 0 0 2px;
        }}
        .preview-overlay-corners::after {{
            bottom: 0; right: 0; border-width: 0 2px 2px 0;
        }}

        /* Palette Swatches */
        .palette-section {{
            background: rgba(31, 40, 51, 0.2);
            border: 1px solid #1f2833;
            border-radius: 6px;
            padding: 12px;
        }}

        .section-title {{
            font-family: 'Orbitron', sans-serif;
            font-size: 13px;
            font-weight: 700;
            color: #66fcf1;
            text-transform: uppercase;
            letter-spacing: 2px;
            margin-bottom: 10px;
            border-left: 3px solid #66fcf1;
            padding-left: 8px;
        }}

        .swatches-grid {{
            display: flex;
            gap: 10px;
        }}

        .swatch-item {{
            display: flex;
            flex-direction: column;
            align-items: center;
            flex: 1;
        }}

        .color-block {{
            width: 100%;
            height: 30px;
            border-radius: 4px;
            border: 1px solid #1f2833;
            box-shadow: 0 4px 6px rgba(0,0,0,0.3);
            margin-bottom: 4px;
        }}

        .color-label {{
            font-size: 11px;
            font-weight: 600;
            text-transform: uppercase;
            color: #ffffff;
            text-align: center;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            max-width: 100%;
        }}

        .color-hex {{
            font-size: 9px;
            color: #858687;
            font-family: monospace;
        }}

        /* Concept / Lore Section */
        .concept-section {{
            background: rgba(31, 40, 51, 0.2);
            border: 1px solid #1f2833;
            border-radius: 6px;
            padding: 14px;
            flex: 1;
        }}

        .concept-text {{
            font-size: 14px;
            line-height: 1.45;
            color: #c5c6c7;
        }}

        /* Right Column: Kits & Core Archetype */
        .right-column {{
            padding: 20px;
            display: flex;
            flex-direction: column;
            gap: 15px;
            height: calc(1200px - 140px); /* Fill space perfectly */
        }}

        .archetype-banner {{
            background: linear-gradient(90deg, rgba(31, 40, 51, 0.6) 0%, rgba(11, 12, 16, 0.1) 100%);
            border-left: 4px solid #66fcf1;
            padding: 15px;
            border-radius: 0 8px 8px 0;
        }}

        .archetype-title {{
            font-family: 'Orbitron', sans-serif;
            font-size: 16px;
            font-weight: 700;
            color: #ffffff;
            text-transform: uppercase;
            letter-spacing: 2px;
            margin-bottom: 6px;
        }}

        .archetype-desc {{
            font-size: 14px;
            line-height: 1.35;
            color: #c5c6c7;
        }}

        /* Ability Kit Rows */
        .abilities-section {{
            display: flex;
            flex-direction: column;
            gap: 8px;
            flex: 1;
        }}

        .ability-row {{
            background: rgba(31, 40, 51, 0.25);
            border: 1px solid #1f2833;
            border-radius: 6px;
            padding: 10px 15px;
            display: grid;
            grid-template-columns: 90px 180px 1fr;
            align-items: center;
            transition: all 0.3s ease;
        }}

        .ability-row:hover {{
            border-color: #66fcf1;
            background: rgba(31, 40, 51, 0.4);
        }}

        .slot-keycap {{
            font-family: 'Orbitron', sans-serif;
            background: #1f2833;
            color: #66fcf1;
            border: 1px solid #66fcf1;
            width: 75px;
            height: 28px;
            display: flex;
            justify-content: center;
            align-items: center;
            font-weight: 900;
            font-size: 12px;
            border-radius: 4px;
            box-shadow: 0 0 10px rgba(102, 252, 241, 0.1);
            text-transform: uppercase;
        }}

        .ability-info {{
            display: flex;
            flex-direction: column;
            padding-right: 15px;
        }}

        .ability-name {{
            font-size: 16px;
            font-weight: 700;
            color: #ffffff;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            line-height: 1.2;
        }}

        .ability-type {{
            font-size: 11px;
            text-transform: uppercase;
            font-weight: 600;
            letter-spacing: 1px;
            margin-top: 2px;
        }}

        .type-melee {{ color: #ff5252; }}
        .type-projectile {{ color: #40c4ff; }}
        .type-mobility {{ color: #69f0ae; }}
        .type-charge {{ color: #ffd740; }}
        .type-ultimate {{ color: #e040fb; }}
        .type-artillery {{ color: #ff5252; }}
        .type-buff {{ color: #ffab40; }}
        .type-aoe {{ color: #b2ff59; }}
        .type-engage {{ color: #ff6e40; }}
        .type-finisher {{ color: #ea80fc; }}

        .ability-desc {{
            font-size: 14px;
            line-height: 1.35;
            color: #c5c6c7;
        }}

        /* Footer Branding */
        .footer {{
            background: #0b0c10;
            border-top: 1px solid #1f2833;
            padding: 12px 30px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            z-index: 5;
            font-size: 12px;
            letter-spacing: 1px;
            text-transform: uppercase;
            color: #858687;
        }}

        .footer-branding {{
            font-family: 'Orbitron', sans-serif;
            font-weight: 700;
            color: #66fcf1;
        }}
    </style>
</head>
<body>
    <div class="header">
        <div class="header-title-area">
            <span class="header-game">SLOPArena // Roster Registry</span>
            <span class="header-name">{name}</span>
            <span class="header-tagline">{title}</span>
        </div>
        <div class="status-badge">{status}</div>
    </div>

    <div class="main-container">
        <div class="left-column">
            <div class="character-preview-card">
                <div class="preview-overlay-corners"></div>
                <img class="character-image" src="{image_path}" alt="{name}">
            </div>

            <div class="palette-section">
                <h3 class="section-title">Visual Palette</h3>
                <div class="swatches-grid">
                    {palette_swatches}
                </div>
            </div>

            <div class="concept-section">
                <h3 class="section-title">Concept & Theme</h3>
                <p class="concept-text">{concept_text}</p>
            </div>
        </div>

        <div class="right-column">
            <div class="archetype-banner">
                <div class="archetype-title">Combat Archetype</div>
                <p class="archetype-desc">{archetype}</p>
            </div>

            <div class="abilities-section">
                <h3 class="section-title" style="margin-bottom: 3px;">Activated Ability Kit</h3>
                {ability_rows}
            </div>
        </div>
    </div>

    <div class="footer">
        <div>System Version v1.1.0 (Density Optimized)</div>
        <div>Rendered: 2026-07-03</div>
        <div class="footer-branding">Oh My Pi // SLOPArena</div>
    </div>
</body>
</html>
"""

def parse_markdown(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Extract YAML front matter
    yaml_match = re.match(r'^---\s*\n(.*?)\n---\s*\n', content, re.DOTALL)
    if not yaml_match:
        print(f"No YAML front matter found in {filepath}")
        return None, None
        
    front_matter_str = yaml_match.group(1)
    data = yaml.safe_load(front_matter_str)
    
    # Get remaining content as body
    body_content = content[yaml_match.end():]
    
    # Extraction of Concept section
    concept_match = re.search(r'## Concept\s*\n\s*(.*?)\n\s*(##|$)', body_content, re.DOTALL)
    concept_text = concept_match.group(1).strip() if concept_match else ""
    
    return data, concept_text

def build_swatches(palette):
    html = ""
    for name, hexcode in palette.items():
        html += f"""
        <div class="swatch-item">
            <div class="color-block" style="background-color: {hexcode};"></div>
            <span class="color-label">{name}</span>
            <span class="color-hex">{hexcode}</span>
        </div>
        """
    return html

def build_ability_rows(kit):
    html = ""
    for item in kit:
        slot = item.get('slot', 'LMB')
        name = item.get('name', 'Ability')
        type_lbl = item.get('type', 'melee')
        desc = item.get('description', '')
        
        html += f"""
        <div class="ability-row">
            <div class="slot-keycap">{slot}</div>
            <div class="ability-info">
                <span class="ability-name">{name}</span>
                <span class="ability-type type-{type_lbl}">{type_lbl}</span>
            </div>
            <p class="ability-desc">{desc}</p>
        </div>
        """
    return html

def generate_html(md_path, out_html_path, image_name):
    data, concept = parse_markdown(md_path)
    if not data:
        return
        
    swatches_html = build_swatches(data.get('palette', {}))
    abilities_html = build_ability_rows(data.get('kit', []))
    
    formatted = HTML_TEMPLATE.format(
        name=data.get('name', 'Unknown'),
        title=data.get('title', 'Unknown'),
        status=data.get('status', 'Concept'),
        archetype=data.get('archetype', 'Brawler'),
        image_path="images/" + image_name,
        palette_swatches=swatches_html,
        concept_text=concept,
        ability_rows=abilities_html
    )
    
    with open(out_html_path, 'w', encoding='utf-8') as f:
        f.write(formatted)
    print(f"Generated HTML sheet: {out_html_path}")

def html_to_png(html_path, png_path):
    """Render an HTML file to a full-page PNG via Playwright headless Chromium."""
    from playwright.sync_api import sync_playwright
    abs_html = os.path.abspath(html_path)
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={"width": 1200, "height": 1})
        page.goto(f"file://{abs_html}")
        page.screenshot(path=png_path, full_page=True)
        browser.close()
    print(f"Generated PNG sheet: {png_path}")


if __name__ == "__main__":
    os.makedirs("docs/characters", exist_ok=True)
    for name, img in [("manki", "manki_demo.jpg"), ("fightguy", "fightguy_front.jpg")]:
        md = f"docs/characters/{name}.md"
        html = f"docs/characters/{name}_sheet.html"
        png = f"docs/characters/{name}_sheet.png"
        generate_html(md, html, img)
        html_to_png(html, png)
