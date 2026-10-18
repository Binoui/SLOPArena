#!/usr/bin/env python3
"""Exposure report for stage capture passes (sloparena.stage.views.capture / .cameras).

Prints, for every PNG in each pass directory, the mean brightness (Rec. 709 luma of the
sRGB values, 0-255), the share of clipped pixels (any channel at 250 or above) and of
crushed pixels (luma at 8 or below), and flags views that are probably overexposed or
underexposed. With --against, it also compares two passes view by view, for example the
same stage under Training's scene lighting and under PvP's:

    python3 tools/capture_exposure.py tmp/<stage>-views/pass-01
    python3 tools/capture_exposure.py tmp/<stage>-views/scene --against tmp/<stage>-views/pvp

The thresholds come from the 2026-10 stage passes: an accepted view clips about 1% of its
pixels, while Log Off and Table Manners under doubled Training lights clipped 44-45%.
A flag is a prompt to look, not a verdict. Exit status is 1 when any view is flagged.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

CLIPPED_LEVEL, CRUSHED_LEVEL = 250, 8
MAX_CLIPPED, MAX_MEAN, MIN_MEAN = 0.10, 150.0, 30.0
MAX_DIFFERENCE = 20.0  # mean-brightness gap between two passes of the same view


def measure(path):
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)
    luma = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    report = {
        "mean": round(float(luma.mean()), 1),
        "clipped": round(float((rgb >= CLIPPED_LEVEL).any(axis=2).mean()), 4),
        "crushed": round(float((luma <= CRUSHED_LEVEL).mean()), 4),
    }
    flags = []
    if report["clipped"] > MAX_CLIPPED:
        flags.append("%.0f%% clipped" % (report["clipped"] * 100))
    if report["mean"] > MAX_MEAN:
        flags.append("bright (mean %.0f)" % report["mean"])
    if report["mean"] < MIN_MEAN:
        flags.append("dark (mean %.0f)" % report["mean"])
    report["flags"] = flags
    return report


def measure_pass(directory):
    names = sorted(n for n in os.listdir(directory) if n.lower().endswith(".png"))
    if not names:
        raise SystemExit("No PNG captures in " + directory)
    return {name: measure(os.path.join(directory, name)) for name in names}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("passes", nargs="+", help="capture pass directories")
    parser.add_argument("--against", help="a second pass of the same views to compare with")
    parser.add_argument("--json", action="store_true", help="print the full report as JSON")
    args = parser.parse_args(argv)

    report = {directory: measure_pass(directory) for directory in args.passes}
    flagged = any(view["flags"] for views in report.values() for view in views.values())
    comparison = {}
    if args.against:
        other = measure_pass(args.against)
        for directory, views in report.items():
            for name, view in views.items():
                if name in other:
                    gap = round(view["mean"] - other[name]["mean"], 1)
                    comparison[name] = {"mean": view["mean"], "againstMean": other[name]["mean"], "difference": gap,
                                        "clipped": view["clipped"], "againstClipped": other[name]["clipped"]}
                    if abs(gap) > MAX_DIFFERENCE:
                        flagged = True
                        comparison[name]["flag"] = "differs by %.0f" % gap

    if args.json:
        print(json.dumps({"passes": report, "comparison": comparison or None, "flagged": flagged}, indent=1))
    else:
        for directory, views in report.items():
            print(directory)
            for name, view in views.items():
                note = ("  <- " + ", ".join(view["flags"])) if view["flags"] else ""
                print("  %-34s mean %5.1f  clipped %5.1f%%  crushed %5.1f%%%s"
                      % (name, view["mean"], view["clipped"] * 100, view["crushed"] * 100, note))
        if comparison:
            print("against", args.against)
            for name, row in comparison.items():
                note = ("  <- " + row["flag"]) if "flag" in row else ""
                print("  %-34s mean %5.1f vs %5.1f  clipped %5.1f%% vs %5.1f%%%s"
                      % (name, row["mean"], row["againstMean"], row["clipped"] * 100, row["againstClipped"] * 100, note))
    return 1 if flagged else 0


if __name__ == "__main__":
    sys.exit(main())
