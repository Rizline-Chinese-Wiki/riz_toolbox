var failures = 0;
foreach (var check in UnityObjectBackingRegression.Cases)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception error) { ++failures; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
}
Console.WriteLine($"Unity backing runtime checks: {UnityObjectBackingRegression.Cases.Length - failures}/{UnityObjectBackingRegression.Cases.Length} passed.");
return failures == 0 ? 0 : 1;
