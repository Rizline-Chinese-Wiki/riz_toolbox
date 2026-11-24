"""Algorithm model only: no production service, Unity, native calls or installation.

Assumptions: stable 8192-object ordering, alternating 4096-element windows;
128 bindings; all candidates equally prioritized, eligible and safely evictable;
one replacement per pass, except initial capacity fill. Compare bounded
admission history 4224 (old) with 50000 (corrected native-array limit).

The selection-loop early exit at order zero is equivalent here because all
priorities and liveness are constant and a later candidate cannot beat zero.
It does not model callbacks, retained native roots, cleanup or eligibility.
"""

from collections import OrderedDict


def model(history_limit, passes=12000):
    length, budget, capacity = 8192, 4096, 128
    sequence = 0
    bindings = OrderedDict()
    history = OrderedDict()
    seen = set()
    first_full_coverage = None
    rows = []

    def admit(identity):
        nonlocal sequence
        sequence += 1
        bindings[identity] = sequence
        history[identity] = sequence
        history.move_to_end(identity)
        seen.add(identity)
        if len(history) > history_limit:
            # Same victim as ordering non-bound history by admission sequence.
            oldest = next(key for key in history if key not in bindings)
            del history[oldest]

    for tick in range(passes):
        start = (tick * budget) % length
        candidates = range(start, start + budget)
        incumbents = tuple(bindings)
        waiting, waiting_order = None, 0
        for identity in candidates:
            if identity in bindings:
                continue
            if len(bindings) < capacity:
                admit(identity)
                continue
            if not incumbents:
                continue
            order = history.get(identity, 0)
            if waiting is None or order < waiting_order:
                waiting, waiting_order = identity, order
                if order == 0:
                    break
        if waiting is not None:
            victim = min(incumbents, key=bindings.get)
            del bindings[victim]
            admit(waiting)
        if len(seen) == length and first_full_coverage is None:
            first_full_coverage = tick + 1
        if tick + 1 in (4096, 5000, 8192, 12000):
            rows.append(
                f"history_limit={history_limit} passes={tick + 1} "
                f"distinct_admitted={len(seen)} history={len(history)} "
                f"tail8191_seen={8191 in seen}"
            )
    rows.append(
        f"history_limit={history_limit} first_full_coverage_pass={first_full_coverage}"
    )
    return rows


if __name__ == "__main__":
    print("ALGORITHM MODEL ONLY; not production-service integration or live game.")
    for history_limit in (4224, 50000):
        for row in model(history_limit):
            print(row, flush=True)
