/**
 * SVG Decomposer - Proper Implementation
 *
 * Uses the polygon-clipping library (Martinez-Rueda-Feito algorithm)
 * for correct boolean operations on polygons.
 *
 * Usage:
 *   node svg-decomposer.js shape1.svg shape2.svg [shape3.svg ...] [-o output_dir]
 */

const fs = require('fs');
const path = require('path');
const polygonClipping = require('polygon-clipping');

// ============================================================
// SVG PARSING
// ============================================================

function parseCircle(attrs) {
    const cx = parseFloat(attrs.match(/cx="([^"]+)"/)?.[1] || 0);
    const cy = parseFloat(attrs.match(/cy="([^"]+)"/)?.[1] || 0);
    const r = parseFloat(attrs.match(/(?:^|\s)r="([^"]+)"/)?.[1] || 0);
    if (r <= 0) return null;

    const pts = [];
    const n = 72; // 72 segments for smooth circles
    for (let i = 0; i < n; i++) {
        const a = 2 * Math.PI * i / n;
        pts.push([cx + r * Math.cos(a), cy + r * Math.sin(a)]);
    }
    return [pts];
}

function parseEllipse(attrs) {
    const cx = parseFloat(attrs.match(/cx="([^"]+)"/)?.[1] || 0);
    const cy = parseFloat(attrs.match(/cy="([^"]+)"/)?.[1] || 0);
    const rx = parseFloat(attrs.match(/rx="([^"]+)"/)?.[1] || 0);
    const ry = parseFloat(attrs.match(/ry="([^"]+)"/)?.[1] || 0);
    if (rx <= 0 || ry <= 0) return null;

    const pts = [];
    const n = 72;
    for (let i = 0; i < n; i++) {
        const a = 2 * Math.PI * i / n;
        pts.push([cx + rx * Math.cos(a), cy + ry * Math.sin(a)]);
    }
    return [pts];
}

function parseRect(attrs) {
    const x = parseFloat(attrs.match(/(?:^|\s)x="([^"]+)"/)?.[1] || 0);
    const y = parseFloat(attrs.match(/(?:^|\s)y="([^"]+)"/)?.[1] || 0);
    const w = parseFloat(attrs.match(/width="([^"]+)"/)?.[1] || 0);
    const h = parseFloat(attrs.match(/height="([^"]+)"/)?.[1] || 0);
    if (w <= 0 || h <= 0) return null;
    return [[[x, y], [x + w, y], [x + w, y + h], [x, y + h]]];
}

function parsePolygonPoints(pointsStr) {
    const coords = pointsStr.trim().split(/[\s,]+/).map(Number);
    const pts = [];
    for (let i = 0; i < coords.length; i += 2) {
        pts.push([coords[i], coords[i + 1]]);
    }
    return pts.length >= 3 ? [pts] : null;
}

function parsePath(d) {
    const pts = [];
    const cmds = d.match(/[MmLlHhVvCcSsQqTtAaZz][^MmLlHhVvCcSsQqTtAaZz]*/g) || [];
    let x = 0, y = 0, startX = 0, startY = 0;
    let lastCx = 0, lastCy = 0; // For smooth curves

    for (const cmd of cmds) {
        const type = cmd[0];
        const args = cmd.slice(1).trim().split(/[\s,]+/).filter(Boolean).map(Number);

        switch (type) {
            case 'M':
                x = args[0]; y = args[1];
                startX = x; startY = y;
                pts.push([x, y]);
                for (let i = 2; i < args.length; i += 2) {
                    x = args[i]; y = args[i + 1];
                    pts.push([x, y]);
                }
                break;
            case 'm':
                x += args[0]; y += args[1];
                startX = x; startY = y;
                pts.push([x, y]);
                for (let i = 2; i < args.length; i += 2) {
                    x += args[i]; y += args[i + 1];
                    pts.push([x, y]);
                }
                break;
            case 'L':
                for (let i = 0; i < args.length; i += 2) {
                    x = args[i]; y = args[i + 1];
                    pts.push([x, y]);
                }
                break;
            case 'l':
                for (let i = 0; i < args.length; i += 2) {
                    x += args[i]; y += args[i + 1];
                    pts.push([x, y]);
                }
                break;
            case 'H':
                for (const v of args) { x = v; pts.push([x, y]); }
                break;
            case 'h':
                for (const v of args) { x += v; pts.push([x, y]); }
                break;
            case 'V':
                for (const v of args) { y = v; pts.push([x, y]); }
                break;
            case 'v':
                for (const v of args) { y += v; pts.push([x, y]); }
                break;
            case 'C':
                for (let i = 0; i < args.length; i += 6) {
                    const bezPts = cubicBezier(x, y, args[i], args[i+1], args[i+2], args[i+3], args[i+4], args[i+5]);
                    pts.push(...bezPts);
                    lastCx = args[i+2]; lastCy = args[i+3];
                    x = args[i+4]; y = args[i+5];
                }
                break;
            case 'c':
                for (let i = 0; i < args.length; i += 6) {
                    const bezPts = cubicBezier(x, y, x+args[i], y+args[i+1], x+args[i+2], y+args[i+3], x+args[i+4], y+args[i+5]);
                    pts.push(...bezPts);
                    lastCx = x + args[i+2]; lastCy = y + args[i+3];
                    x += args[i+4]; y += args[i+5];
                }
                break;
            case 'S':
                for (let i = 0; i < args.length; i += 4) {
                    const cx1 = 2*x - lastCx, cy1 = 2*y - lastCy;
                    const bezPts = cubicBezier(x, y, cx1, cy1, args[i], args[i+1], args[i+2], args[i+3]);
                    pts.push(...bezPts);
                    lastCx = args[i]; lastCy = args[i+1];
                    x = args[i+2]; y = args[i+3];
                }
                break;
            case 's':
                for (let i = 0; i < args.length; i += 4) {
                    const cx1 = 2*x - lastCx, cy1 = 2*y - lastCy;
                    const bezPts = cubicBezier(x, y, cx1, cy1, x+args[i], y+args[i+1], x+args[i+2], y+args[i+3]);
                    pts.push(...bezPts);
                    lastCx = x + args[i]; lastCy = y + args[i+1];
                    x += args[i+2]; y += args[i+3];
                }
                break;
            case 'Q':
                for (let i = 0; i < args.length; i += 4) {
                    const bezPts = quadBezier(x, y, args[i], args[i+1], args[i+2], args[i+3]);
                    pts.push(...bezPts);
                    lastCx = args[i]; lastCy = args[i+1];
                    x = args[i+2]; y = args[i+3];
                }
                break;
            case 'q':
                for (let i = 0; i < args.length; i += 4) {
                    const bezPts = quadBezier(x, y, x+args[i], y+args[i+1], x+args[i+2], y+args[i+3]);
                    pts.push(...bezPts);
                    lastCx = x + args[i]; lastCy = y + args[i+1];
                    x += args[i+2]; y += args[i+3];
                }
                break;
            case 'T':
                for (let i = 0; i < args.length; i += 2) {
                    const cx = 2*x - lastCx, cy = 2*y - lastCy;
                    const bezPts = quadBezier(x, y, cx, cy, args[i], args[i+1]);
                    pts.push(...bezPts);
                    lastCx = cx; lastCy = cy;
                    x = args[i]; y = args[i+1];
                }
                break;
            case 't':
                for (let i = 0; i < args.length; i += 2) {
                    const cx = 2*x - lastCx, cy = 2*y - lastCy;
                    const bezPts = quadBezier(x, y, cx, cy, x+args[i], y+args[i+1]);
                    pts.push(...bezPts);
                    lastCx = cx; lastCy = cy;
                    x += args[i]; y += args[i+1];
                }
                break;
            case 'A':
            case 'a':
                for (let i = 0; i < args.length; i += 7) {
                    const rx = args[i], ry = args[i+1], rotation = args[i+2];
                    const largeArc = args[i+3], sweep = args[i+4];
                    const endX = type === 'A' ? args[i+5] : x + args[i+5];
                    const endY = type === 'A' ? args[i+6] : y + args[i+6];
                    const arcPts = arcToBezier(x, y, rx, ry, rotation, largeArc, sweep, endX, endY);
                    pts.push(...arcPts);
                    x = endX; y = endY;
                }
                break;
            case 'Z':
            case 'z':
                x = startX; y = startY;
                break;
        }
    }

    // Remove near-duplicate consecutive points
    const filtered = pts.length > 0 ? [pts[0]] : [];
    for (let i = 1; i < pts.length; i++) {
        const prev = filtered[filtered.length - 1];
        if (Math.abs(pts[i][0] - prev[0]) > 0.001 || Math.abs(pts[i][1] - prev[1]) > 0.001) {
            filtered.push(pts[i]);
        }
    }

    return filtered.length >= 3 ? [filtered] : null;
}

function cubicBezier(x0, y0, x1, y1, x2, y2, x3, y3, segments = 16) {
    const pts = [];
    for (let i = 1; i <= segments; i++) {
        const t = i / segments;
        const mt = 1 - t;
        pts.push([
            mt*mt*mt*x0 + 3*mt*mt*t*x1 + 3*mt*t*t*x2 + t*t*t*x3,
            mt*mt*mt*y0 + 3*mt*mt*t*y1 + 3*mt*t*t*y2 + t*t*t*y3
        ]);
    }
    return pts;
}

function quadBezier(x0, y0, x1, y1, x2, y2, segments = 16) {
    const pts = [];
    for (let i = 1; i <= segments; i++) {
        const t = i / segments;
        const mt = 1 - t;
        pts.push([
            mt*mt*x0 + 2*mt*t*x1 + t*t*x2,
            mt*mt*y0 + 2*mt*t*y1 + t*t*y2
        ]);
    }
    return pts;
}

function arcToBezier(x1, y1, rx, ry, rotation, largeArc, sweep, x2, y2) {
    // Simplified arc - approximate with line segments
    // For a proper implementation, convert to center parameterization
    const pts = [];
    const segments = 20;

    // Simple linear approximation for now
    // TODO: Implement proper arc-to-bezier conversion
    for (let i = 1; i <= segments; i++) {
        const t = i / segments;
        pts.push([x1 + t * (x2 - x1), y1 + t * (y2 - y1)]);
    }
    return pts;
}

function parseSVG(content) {
    const shapes = [];
    let viewBox = null;

    // Parse viewBox
    const vbMatch = content.match(/viewBox="([^"]+)"/);
    if (vbMatch) {
        const [x, y, w, h] = vbMatch[1].split(/[\s,]+/).map(Number);
        viewBox = { x, y, width: w, height: h };
    }

    // Parse circles
    let match;
    const circleRe = /<circle([^>]*)\/?\s*>/gi;
    while ((match = circleRe.exec(content))) {
        const poly = parseCircle(match[1]);
        if (poly) shapes.push(poly);
    }

    // Parse ellipses
    const ellipseRe = /<ellipse([^>]*)\/?\s*>/gi;
    while ((match = ellipseRe.exec(content))) {
        const poly = parseEllipse(match[1]);
        if (poly) shapes.push(poly);
    }

    // Parse rects
    const rectRe = /<rect([^>]*)\/?\s*>/gi;
    while ((match = rectRe.exec(content))) {
        const poly = parseRect(match[1]);
        if (poly) shapes.push(poly);
    }

    // Parse polygons
    const polygonRe = /<polygon[^>]*points="([^"]+)"[^>]*\/?\s*>/gi;
    while ((match = polygonRe.exec(content))) {
        const poly = parsePolygonPoints(match[1]);
        if (poly) shapes.push(poly);
    }

    // Parse paths
    const pathRe = /<path[^>]*\sd="([^"]+)"[^>]*\/?\s*>/gi;
    while ((match = pathRe.exec(content))) {
        const poly = parsePath(match[1]);
        if (poly) shapes.push(poly);
    }

    return { shapes, viewBox };
}

// ============================================================
// REGION COMPUTATION
// ============================================================

function polyArea(multiPoly) {
    if (!multiPoly || multiPoly.length === 0) return 0;
    let total = 0;
    for (const poly of multiPoly) {
        if (!poly || poly.length === 0) continue;
        const ring = poly[0];
        let a = 0;
        for (let i = 0; i < ring.length; i++) {
            const j = (i + 1) % ring.length;
            a += ring[i][0] * ring[j][1] - ring[j][0] * ring[i][1];
        }
        total += Math.abs(a) / 2;
    }
    return total;
}

function* combinations(arr, size) {
    function* helper(start, combo) {
        if (combo.length === size) {
            yield [...combo];
            return;
        }
        for (let i = start; i < arr.length; i++) {
            combo.push(arr[i]);
            yield* helper(i + 1, combo);
            combo.pop();
        }
    }
    yield* helper(0, []);
}

function computeAllRegions(shapes) {
    const n = shapes.length;
    const regions = [];

    console.log(`\nComputing all ${Math.pow(2, n) - 1} possible regions for ${n} shapes...\n`);

    for (let size = 1; size <= n; size++) {
        for (const combo of combinations([...Array(n).keys()], size)) {
            const label = combo.length === 1
                ? `S${combo[0] + 1}Only`
                : combo.map(i => `S${i + 1}`).join('∩') + 'Only';

            // Intersection of all shapes in the combination
            let region = shapes[combo[0]];
            for (let i = 1; i < combo.length; i++) {
                try {
                    region = polygonClipping.intersection(region, shapes[combo[i]]);
                } catch (e) {
                    region = [];
                }
                if (!region || region.length === 0) break;
            }

            if (!region || region.length === 0) {
                console.log(`  ${label}: empty (shapes don't overlap)`);
                continue;
            }

            // Subtract all shapes NOT in the combination
            for (let i = 0; i < n; i++) {
                if (combo.includes(i)) continue;
                try {
                    region = polygonClipping.difference(region, shapes[i]);
                } catch (e) {
                    region = [];
                }
                if (!region || region.length === 0) break;
            }

            if (region && region.length > 0) {
                const area = polyArea(region);
                if (area > 0.01) {
                    console.log(`  ${label}: ${region.length} polygon(s), area = ${area.toFixed(2)}`);
                    regions.push({ label, indices: combo, polygons: region });
                } else {
                    console.log(`  ${label}: empty (negligible area)`);
                }
            } else {
                console.log(`  ${label}: empty after subtraction`);
            }
        }
    }

    return regions;
}

// ============================================================
// SVG GENERATION
// ============================================================

const COLORS = ['#e63946', '#2a9d8f', '#457b9d', '#f4a261', '#9b59b6'];

function mixColors(indices) {
    if (indices.length === 1) return COLORS[indices[0] % COLORS.length];
    let r = 0, g = 0, b = 0;
    for (const idx of indices) {
        const hex = COLORS[idx % COLORS.length];
        r += parseInt(hex.slice(1, 3), 16);
        g += parseInt(hex.slice(3, 5), 16);
        b += parseInt(hex.slice(5, 7), 16);
    }
    r = Math.round(r / indices.length);
    g = Math.round(g / indices.length);
    b = Math.round(b / indices.length);
    return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`;
}

function multiPolyToPath(multiPoly) {
    const paths = [];
    for (const poly of multiPoly) {
        for (const ring of poly) {
            if (ring.length < 3) continue;
            paths.push('M ' + ring.map(p => `${p[0].toFixed(2)} ${p[1].toFixed(2)}`).join(' L ') + ' Z');
        }
    }
    return paths.join(' ');
}

function getBounds(shapes) {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const shape of shapes) {
        for (const poly of shape) {
            for (const ring of poly) {
                for (const pt of ring) {
                    minX = Math.min(minX, pt[0]);
                    minY = Math.min(minY, pt[1]);
                    maxX = Math.max(maxX, pt[0]);
                    maxY = Math.max(maxY, pt[1]);
                }
            }
        }
    }
    return { minX, minY, maxX, maxY };
}

function generateRegionSVG(region, viewBox) {
    const color = mixColors(region.indices);
    const pathD = multiPolyToPath(region.polygons);

    return `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox.x} ${viewBox.y} ${viewBox.width} ${viewBox.height}">
  <title>${region.label}</title>
  <path d="${pathD}" fill="${color}" stroke="#333" stroke-width="1"/>
</svg>`;
}

function generateOverviewSVG(regions, shapes, viewBox) {
    let content = '';

    // Original shape outlines (dashed)
    for (let i = 0; i < shapes.length; i++) {
        const pathD = multiPolyToPath(shapes[i]);
        content += `  <path d="${pathD}" fill="none" stroke="${COLORS[i % COLORS.length]}" stroke-width="2" stroke-dasharray="5,3" opacity="0.6"><title>S${i + 1}</title></path>\n`;
    }

    // All computed regions
    for (const region of regions) {
        const color = mixColors(region.indices);
        const pathD = multiPolyToPath(region.polygons);
        content += `  <path d="${pathD}" fill="${color}" stroke="#333" stroke-width="0.5" fill-opacity="0.8"><title>${region.label}</title></path>\n`;
    }

    return `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox.x} ${viewBox.y} ${viewBox.width} ${viewBox.height}">
  <title>SVG Decomposition - All Regions</title>
  <style>path:hover { fill-opacity: 1; stroke-width: 2; }</style>
${content}</svg>`;
}

// ============================================================
// MAIN
// ============================================================

function decomposeSVGs(svgContents, outputDir = './output') {
    console.log('='.repeat(60));
    console.log('SVG DECOMPOSER');
    console.log('Using polygon-clipping (Martinez-Rueda-Feito algorithm)');
    console.log('='.repeat(60));

    if (svgContents.length < 2 || svgContents.length > 5) {
        throw new Error('Please provide 2-5 SVG files');
    }

    console.log(`\nParsing ${svgContents.length} SVG files...\n`);

    // Parse all SVGs
    const shapes = [];
    let viewBox = null;

    for (let i = 0; i < svgContents.length; i++) {
        const { shapes: parsed, viewBox: vb } = parseSVG(svgContents[i]);
        if (parsed.length === 0) {
            console.warn(`Warning: No shapes found in SVG ${i + 1}`);
            continue;
        }
        shapes.push(parsed[0]); // Take first shape from each SVG
        if (!viewBox && vb) viewBox = vb;

        const area = polyArea([parsed[0]]);
        console.log(`  S${i + 1}: ${parsed[0][0].length} vertices, area = ${area.toFixed(2)}`);
    }

    if (shapes.length < 2) {
        throw new Error('Need at least 2 valid shapes to decompose');
    }

    // Default viewBox if not found
    if (!viewBox) {
        const bounds = getBounds(shapes);
        viewBox = {
            x: bounds.minX - 20,
            y: bounds.minY - 20,
            width: bounds.maxX - bounds.minX + 40,
            height: bounds.maxY - bounds.minY + 40
        };
    }

    // Compute all regions
    const regions = computeAllRegions(shapes);

    console.log(`\n${'='.repeat(60)}`);
    console.log(`RESULTS: ${regions.length} non-empty regions`);
    console.log('='.repeat(60));

    // Create output directory
    if (!fs.existsSync(outputDir)) {
        fs.mkdirSync(outputDir, { recursive: true });
    }

    // Generate individual region SVGs
    const outputs = [];
    for (const region of regions) {
        const svg = generateRegionSVG(region, viewBox);
        const filename = region.label.replace(/∩/g, '_intersect_') + '.svg';
        fs.writeFileSync(path.join(outputDir, filename), svg);
        outputs.push({
            label: region.label,
            filename,
            indices: region.indices,
            area: polyArea(region.polygons)
        });
        console.log(`  Created: ${filename}`);
    }

    // Generate overview SVG
    const overview = generateOverviewSVG(regions, shapes, viewBox);
    fs.writeFileSync(path.join(outputDir, 'overview.svg'), overview);
    console.log('  Created: overview.svg');

    // Generate manifest
    const manifest = {
        timestamp: new Date().toISOString(),
        inputCount: svgContents.length,
        algorithm: 'Martinez-Rueda-Feito (polygon-clipping)',
        regions: outputs
    };
    fs.writeFileSync(path.join(outputDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
    console.log('  Created: manifest.json');

    console.log(`\nDone! Output written to: ${outputDir}`);

    return { regions: outputs, manifest };
}

// CLI interface
if (require.main === module) {
    const args = process.argv.slice(2);

    if (args.length < 2 || args.includes('-h') || args.includes('--help')) {
        console.log(`
SVG Decomposer - Break overlapping shapes into distinct regions

Usage:
  node svg-decomposer.js <svg1> <svg2> [svg3] [svg4] [svg5] [-o output_dir]

Options:
  -o, --output   Output directory (default: ./output)
  -h, --help     Show this help message

Example:
  node svg-decomposer.js circle1.svg circle2.svg -o ./venn-output

The tool will generate:
  - Individual SVG files for each distinct region (S1Only, S2Only, S1_intersect_S2Only, etc.)
  - overview.svg showing all regions together
  - manifest.json with metadata
`);
        process.exit(args.includes('-h') || args.includes('--help') ? 0 : 1);
    }

    let outputDir = './output';
    const files = [];

    for (let i = 0; i < args.length; i++) {
        if (args[i] === '-o' || args[i] === '--output') {
            outputDir = args[++i];
        } else if (!args[i].startsWith('-')) {
            files.push(args[i]);
        }
    }

    // Read input files
    const contents = files.map(f => {
        if (!fs.existsSync(f)) {
            console.error(`Error: File not found: ${f}`);
            process.exit(1);
        }
        return fs.readFileSync(f, 'utf8');
    });

    try {
        decomposeSVGs(contents, outputDir);
    } catch (e) {
        console.error(`Error: ${e.message}`);
        process.exit(1);
    }
}

module.exports = { decomposeSVGs, parseSVG };
