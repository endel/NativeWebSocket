#!/usr/bin/env python3

import argparse
import csv
import math
import statistics
import sys
from collections import defaultdict


LOWER_IS_BETTER_METRICS = {
    "avg_ms",
    "avg_frame_time_ms",
    "jitter_stddev_ms",
    "max_ms",
    "median_ms",
    "p95_ms",
    "p99_ms",
    "peak_frame_time_ms",
    "stddev_ms",
    "total_time_ms",
    "frames_to_process",
}


def parse_args():
    parser = argparse.ArgumentParser(
        description="Aggregate one or more benchmark CSV files and compare implementations.")
    parser.add_argument("csv_files", nargs="+", help="Benchmark CSV files to aggregate")
    parser.add_argument("--baseline", help="Implementation to use as the baseline column")
    parser.add_argument("--candidate", help="Implementation to use as the candidate column")
    parser.add_argument("--benchmarks", nargs="*", help="Optional benchmark name filter")
    parser.add_argument("--metrics", nargs="*", help="Optional metric name filter")
    parser.add_argument("--payloads", nargs="*", type=int, help="Optional payload-size filter")
    return parser.parse_args()


def summarize(values):
    values = list(values)
    count = len(values)
    mean = statistics.mean(values)
    median = statistics.median(values)
    stdev = statistics.stdev(values) if count > 1 else 0.0
    return {
        "count": count,
        "mean": mean,
        "median": median,
        "stdev": stdev,
        "min": min(values),
        "max": max(values),
    }


def format_number(value):
    if value is None:
        return "-"

    absolute = abs(value)
    if absolute >= 1000:
        return f"{value:,.0f}"
    if absolute >= 100:
        return f"{value:,.2f}"
    if absolute >= 1:
        return f"{value:,.3f}"
    return f"{value:,.6f}"


def format_percent(value):
    if value is None or math.isinf(value) or math.isnan(value):
        return "-"
    return f"{value:+.2f}%"


def format_payload(payload_size):
    if payload_size == 0:
        return "0"
    if payload_size >= 1024:
        if payload_size % 1024 == 0:
            return f"{payload_size // 1024}KB"
    return f"{payload_size}B"


def is_lower_better(metric):
    return metric in LOWER_IS_BETTER_METRICS or metric.endswith("_ms")


def pick_implementations(all_implementations, baseline, candidate):
    implementations = set(all_implementations)

    if baseline and candidate:
        if baseline not in implementations or candidate not in implementations:
            missing = [name for name in (baseline, candidate) if name not in implementations]
            raise SystemExit(f"Missing implementation(s): {', '.join(missing)}")
        return baseline, candidate

    if "old" in implementations and "new" in implementations:
        return "old", "new"

    if len(implementations) == 2:
        baseline, candidate = sorted(implementations)
        return baseline, candidate

    return None, None


def load_data(csv_files, benchmark_filter, metric_filter, payload_filter):
    grouped = defaultdict(list)
    benchmarks = set()
    metrics = set()
    payloads = set()
    implementations = set()

    for file_path in csv_files:
        with open(file_path, newline="") as source:
            reader = csv.DictReader(source)
            for row in reader:
                benchmark = row["benchmark"]
                metric = row["metric"]
                payload = int(row["payload_size"])
                implementation = row["implementation"]

                if benchmark_filter and benchmark not in benchmark_filter:
                    continue
                if metric_filter and metric not in metric_filter:
                    continue
                if payload_filter and payload not in payload_filter:
                    continue

                value = float(row["value"])
                key = (implementation, benchmark, metric, payload)
                grouped[key].append(value)
                benchmarks.add(benchmark)
                metrics.add(metric)
                payloads.add(payload)
                implementations.add(implementation)

    return grouped, sorted(benchmarks), sorted(metrics), sorted(payloads), sorted(implementations)


def print_comparison(grouped, benchmarks, metrics, payloads, baseline, candidate):
    keys = defaultdict(dict)
    for (implementation, benchmark, metric, payload), values in grouped.items():
        keys[(benchmark, metric, payload)][implementation] = summarize(values)

    printed_anything = False

    for benchmark in benchmarks:
        benchmark_rows = []
        for metric in metrics:
            for payload in payloads:
                summaries = keys.get((benchmark, metric, payload))
                if not summaries:
                    continue

                baseline_summary = summaries.get(baseline)
                candidate_summary = summaries.get(candidate)
                if not baseline_summary and not candidate_summary:
                    continue

                baseline_mean = baseline_summary["mean"] if baseline_summary else None
                candidate_mean = candidate_summary["mean"] if candidate_summary else None
                delta_pct = None
                if baseline_mean not in (None, 0) and candidate_mean is not None:
                    delta_pct = ((candidate_mean / baseline_mean) - 1.0) * 100.0

                winner = "-"
                if baseline_mean is not None and candidate_mean is not None and baseline_mean != candidate_mean:
                    lower_better = is_lower_better(metric)
                    candidate_better = candidate_mean < baseline_mean if lower_better else candidate_mean > baseline_mean
                    winner = candidate if candidate_better else baseline

                benchmark_rows.append({
                    "metric": metric,
                    "payload": payload,
                    "baseline": baseline_summary,
                    "candidate": candidate_summary,
                    "delta_pct": delta_pct,
                    "winner": winner,
                })

        if not benchmark_rows:
            continue

        printed_anything = True
        print(f"\n== {benchmark} ==")
        print(
            f"{'Metric':<24} {'Payload':<8} "
            f"{baseline + ' mean±sd':<28} {candidate + ' mean±sd':<28} {'Delta':>10} {'Winner':>8}")
        print(
            f"{'-' * 24:<24} {'-' * 8:<8} "
            f"{'-' * 28:<28} {'-' * 28:<28} {'-' * 10:>10} {'-' * 8:>8}")

        for row in benchmark_rows:
            baseline_summary = row["baseline"]
            candidate_summary = row["candidate"]

            baseline_text = "-"
            if baseline_summary:
                baseline_text = (
                    f"{format_number(baseline_summary['mean'])}"
                    f" +/- {format_number(baseline_summary['stdev'])}"
                    f" (n={baseline_summary['count']})")

            candidate_text = "-"
            if candidate_summary:
                candidate_text = (
                    f"{format_number(candidate_summary['mean'])}"
                    f" +/- {format_number(candidate_summary['stdev'])}"
                    f" (n={candidate_summary['count']})")

            print(
                f"{row['metric']:<24} {format_payload(row['payload']):<8} "
                f"{baseline_text:<28} {candidate_text:<28} "
                f"{format_percent(row['delta_pct']):>10} {row['winner']:>8}")

    if not printed_anything:
        print("No matching benchmark rows found.")


def print_single_impl_summary(grouped, implementations):
    print("Found implementations:", ", ".join(implementations))
    for implementation in implementations:
        print(f"\n== {implementation} ==")
        print(f"{'Benchmark':<24} {'Metric':<24} {'Payload':<8} {'Mean':>14} {'StdDev':>14} {'Runs':>6}")
        print(f"{'-' * 24:<24} {'-' * 24:<24} {'-' * 8:<8} {'-' * 14:>14} {'-' * 14:>14} {'-' * 6:>6}")
        keys = sorted(
            (key for key in grouped.keys() if key[0] == implementation),
            key=lambda item: (item[1], item[2], item[3]))
        for _, benchmark, metric, payload in keys:
            stats = summarize(grouped[(implementation, benchmark, metric, payload)])
            print(
                f"{benchmark:<24} {metric:<24} {format_payload(payload):<8} "
                f"{format_number(stats['mean']):>14} {format_number(stats['stdev']):>14} {stats['count']:>6}")


def main():
    args = parse_args()

    benchmark_filter = set(args.benchmarks or [])
    metric_filter = set(args.metrics or [])
    payload_filter = set(args.payloads or [])

    grouped, benchmarks, metrics, payloads, implementations = load_data(
        args.csv_files,
        benchmark_filter,
        metric_filter,
        payload_filter,
    )

    if not grouped:
        print("No matching benchmark rows found.", file=sys.stderr)
        return 1

    baseline, candidate = pick_implementations(implementations, args.baseline, args.candidate)
    if baseline and candidate:
        print_comparison(grouped, benchmarks, metrics, payloads, baseline, candidate)
    else:
        print_single_impl_summary(grouped, implementations)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
