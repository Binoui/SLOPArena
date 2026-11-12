using System.Text.Json;
using SlopArena.KitCheck;

if (args is ["mutations", var fixture, var output])
{
    var result = MutationContract.Run(Path.Combine(fixture, "mutation-inputs.json"), Path.Combine(fixture, "mutation-edges.json"), Console.WriteLine);
    File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine($"{result.Sum(x => x.Cases)} cases, {result.Sum(x => x.Successful)} successful, {result.Sum(x => x.RoundTrips)} round trips, {result.Sum(x => x.UnusedWarnings)} unused-cue warnings.");
    return 0;
}
if (args is ["fields", var fieldFixture, var fieldOutput])
{
    var inputs = JsonSerializer.Deserialize<MutationInput[]>(File.ReadAllText(Path.Combine(fieldFixture, "mutation-inputs.json")))!;
    var result = FieldContract.Run(inputs, Console.WriteLine);
    File.WriteAllText(fieldOutput, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine($"{result.Count} schemas, {result.Sum(x => x.Objects)} objects, {result.Sum(x => x.Removals)} removals, {result.Sum(x => x.DefaultInsertions)} default insertions.");
    return 0;
}
Console.Error.WriteLine("Usage: CharacterContractBaseline mutations|fields <fixture-directory> <output-json>");
return 2;
