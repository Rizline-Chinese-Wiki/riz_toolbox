"""Independent exact-rational IEEE-754 binary32 oracle for the frozen ring X cases.

This consumes no game binary, runtime data, or production code. Decimal fixture
inputs are rounded once to binary32; every native arithmetic edge is then
rounded from an exact Fraction with ties-to-even before the next operation.
"""
from fractions import Fraction
import json
from pathlib import Path


def p2(power):
    return Fraction(1 << power) if power >= 0 else Fraction(1, 1 << -power)


def nearest_even(value):
    whole, remainder = divmod(value.numerator, value.denominator)
    if 2 * remainder > value.denominator or (
        2 * remainder == value.denominator and whole & 1
    ):
        whole += 1
    return whole


def binary32(value):
    value = Fraction(value)
    sign = 0x80000000 if value < 0 else 0
    magnitude = abs(value)
    if magnitude == 0:
        return sign
    exponent = magnitude.numerator.bit_length() - magnitude.denominator.bit_length()
    while magnitude < p2(exponent):
        exponent -= 1
    while magnitude >= p2(exponent + 1):
        exponent += 1
    if exponent < -126:
        significand = nearest_even(magnitude / p2(-149))
        if significand < 1 << 23:
            return sign | significand
        return sign | 0x00800000
    significand = nearest_even(magnitude / p2(exponent - 23))
    if significand == 1 << 24:
        significand >>= 1
        exponent += 1
    if exponent > 127:
        raise OverflowError("binary32 operation overflowed")
    return sign | ((exponent + 127) << 23) | (significand - (1 << 23))


def value(bits):
    sign = -1 if bits & 0x80000000 else 1
    power = (bits >> 23) & 255
    digits = bits & 0x007FFFFF
    if power == 0:
        return sign * digits * p2(-149)
    if power == 255:
        raise ValueError("nonfinite oracle input")
    return sign * ((1 << 23) + digits) * p2(power - 150)


def f(decimal):
    return binary32(Fraction(str(decimal)))


def op(kind, left, right):
    a, b = value(left), value(right)
    if kind == "add":
        exact = a + b
    elif kind == "sub":
        exact = a - b
    elif kind == "mul":
        exact = a * b
    elif kind == "div":
        exact = a / b
    else:
        raise ValueError(kind)
    return binary32(exact)


def ease(q, samples):
    scaled = op("mul", q, f(100))
    index = int(value(scaled))  # cvttss2si, toward zero
    if index < 0:
        raw = samples[0]
    elif index >= 100:
        raw = samples[100]
    else:
        fraction = op("sub", scaled, f(index))
        difference = op("sub", samples[index + 1], samples[index])
        raw = op("add", op("mul", fraction, difference), samples[index])
    clamped = f(0) if value(raw) < 0 else f(1) if value(raw) > 1 else raw
    return raw, clamped


def project(case, samples):
    if case["branch"] == "static":
        s, e = f(case["startFloor"]), f(case["endFloor"])
        q = op("div", op("sub", f(case["canvasFloor"]), s), op("sub", e, s))
        raw, clamped = ease(q, samples)
        dx = op("sub", f(case["endX"]), f(case["startX"]))
        mixed = op("add", op("mul", dx, clamped), f(case["startX"]))
        x = op("add", op("mul", mixed, f(case["screenWidth"])), f(case["canvasX"]))
    else:
        sy, ey = f(case["startY"]), f(case["endY"])
        q = op("div", op("sub", f(case["ringY"]), sy), op("sub", ey, sy))
        raw, clamped = ease(q, samples)
        dx = op("sub", f(case["endX"]), f(case["startX"]))
        mixed = op("add", op("mul", dx, clamped), f(case["startX"]))
        x = op("sub", op("div", op("div", mixed, f(case["cameraScale"])), f(case["sceneScale"])), f(case["cameraX"]))
    return {key: f"{bits:08x}" for key, bits in
            (("rawProgress", q), ("easeBeforeClamp", raw), ("clampedEase", clamped), ("localX", x))}


def main():
    samples = [f("0.25") for _ in range(101)]
    for index, decimal in {1: "0.75", 25: "1.5", 50: "0.2", 51: "0.8", 75: "-0.25", 100: "1.25"}.items():
        samples[index] = f(decimal)
    cases = [
        dict(name="static_interpolation", branch="static", startFloor="1", endFloor="3",
             canvasFloor="2.01", startX="-1.25", endX="3.5", screenWidth="1.3", canvasX="-0.125"),
        dict(name="static_negative_fraction", branch="static", startFloor="0", endFloor="1",
             canvasFloor="-0.005", startX="1", endX="9", screenWidth="2", canvasX="3"),
        dict(name="static_overshoot", branch="static", startFloor="0", endFloor="1",
             canvasFloor="0.25", startX="-1", endX="2", screenWidth="2", canvasX="3"),
        dict(name="dynamic_interpolation", branch="dynamic", ringY="2.01", startY="1", endY="3",
             startX="-1.25", endX="3.5", cameraScale="1.25", sceneScale="0.8", cameraX="0.3"),
        dict(name="dynamic_negative_fraction", branch="dynamic", ringY="-0.005", startY="0", endY="1",
             startX="1", endX="9", cameraScale="2", sceneScale="4", cameraX="0.5"),
        dict(name="dynamic_undershoot", branch="dynamic", ringY="0.75", startY="0", endY="1",
             startX="-1", endX="2", cameraScale="1", sceneScale="1", cameraX="0"),
        dict(name="dynamic_high_endpoint", branch="dynamic", ringY="1.25", startY="0", endY="1",
             startX="-1", endX="2", cameraScale="1", sceneScale="1", cameraX="0"),
    ]
    result = {case["name"]: project(case, samples) for case in cases}
    destination = Path(__file__).with_name("single-bit-goldens.json")
    destination.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(destination)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
