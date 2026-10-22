using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlopArena.Client.UI;
using SlopArena.Client.Camera;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools
{
    public static class SlopArenaSettingsCommands
    {
        [CliCommand(
            "sloparena.settings.inspect",
            "Inspect supported player preferences from the existing loaded settings owner.",
            MainThreadRequired = true,
            Tags = new[] { "settings", "preferences" })]
        public static SlopArenaSettingsCommandResult Inspect()
        {
            if (!TryResolveOwner(out ClientSettingsService owner, out string ownerError))
                return Failure(ownerError);

            var snapshot = ReadSnapshot(owner);
            return new SlopArenaSettingsCommandResult
            {
                Success = true,
                Before = snapshot,
                After = snapshot,
                PersistenceStatus = "not-written",
                Persisted = false,
                Diagnostics = new List<SlopArenaSettingsDiagnostic>()
            };
        }

        [CliCommand(
            "sloparena.settings.apply",
            "Validate and optionally apply a bounded JSON patch of supported player preferences.",
            MainThreadRequired = true,
            Tags = new[] { "settings", "preferences" })]
        public static SlopArenaSettingsCommandResult Apply(
            [CliArg("patch", "JSON object containing one or more supported preference values.", Required = true)] string patch,
            [CliArg("dry-run", "Validate and preview only; no settings setters run.")] bool dryRun = true,
            [CliArg("confirm", "Explicitly authorize applying and persisting this patch.")] bool confirm = false)
        {
            if (!TryParsePatch(patch, out SettingsPatch parsed, out string parseError))
                return Failure(parseError);
            var requested = ToRequested(parsed);
            if (!dryRun && !confirm)
                return Failure("Applying preferences requires --confirm true; use --dry-run true to preview without changes.", requested);

            if (!TryResolveOwner(out ClientSettingsService owner, out string ownerError))
                return Failure(ownerError, requested);

            var before = ReadSnapshot(owner);
            var proposed = ApplyPatch(before, parsed);
            if (dryRun)
                return new SlopArenaSettingsCommandResult
                {
                    Success = true,
                    DryRun = true,
                    Requested = requested,
                    Before = before,
                    Proposed = proposed,
                    After = before,
                    PersistenceStatus = "not-written",
                    Persisted = false,
                    Diagnostics = new List<SlopArenaSettingsDiagnostic>()
                };

            var result = new SlopArenaSettingsCommandResult
            {
                DryRun = false,
                Requested = requested,
                Before = before,
                Proposed = proposed,
                AppliedFields = new List<string>(),
                PersistenceStatus = "not-attempted",
                Diagnostics = new List<SlopArenaSettingsDiagnostic>()
            };
            try
            {
                if (parsed.UiScale.HasValue) { owner.SetUiScale(parsed.UiScale.Value); result.AppliedFields.Add("uiScale"); }
                if (parsed.TargetOpacity.HasValue) { owner.SetTargetOpacity(parsed.TargetOpacity.Value); result.AppliedFields.Add("targetOpacity"); }
                if (parsed.ShowOverheadDamage.HasValue) { owner.SetShowOverheadDamage(parsed.ShowOverheadDamage.Value); result.AppliedFields.Add("showOverheadDamage"); }
                if (parsed.ReducedFlashing.HasValue) { owner.SetReducedFlashing(parsed.ReducedFlashing.Value); result.AppliedFields.Add("reducedFlashing"); }
                if (parsed.ScreenShake.HasValue) { owner.SetScreenShake(parsed.ScreenShake.Value); result.AppliedFields.Add("screenShake"); }
                if (parsed.RecordSessionLog.HasValue) { owner.SetRecordSessionLog(parsed.RecordSessionLog.Value); result.AppliedFields.Add("recordSessionLog"); }
                if (parsed.CameraStyle.HasValue) { owner.SetCameraStyle(parsed.CameraStyle.Value); result.AppliedFields.Add("cameraStyle"); }
                result.PersistenceStatus = "save-failed-or-partial";
                owner.Save();
                result.Success = true;
                result.Persisted = true;
                result.PersistenceStatus = "saved";
            }
            catch (Exception exception)
            {
                result.Success = false;
                if (result.PersistenceStatus == "not-attempted")
                    result.PersistenceStatus = "unknown-after-setter-failure";
                result.Diagnostics.Add(new SlopArenaSettingsDiagnostic
                {
                    Code = "apply_failed",
                    Message = exception.Message
                });
            }

            try { result.After = ReadSnapshot(owner); }
            catch (Exception exception)
            {
                result.After = null;
                result.Success = false;
                result.Diagnostics.Add(new SlopArenaSettingsDiagnostic
                {
                    Code = "snapshot_failed",
                    Message = exception.Message
                });
            }
            return result;
        }

        internal static bool TryParsePatch(string json, out SettingsPatch patch, out string error)
        {
            patch = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "patch must be a non-empty JSON object.";
                return false;
            }

            JObject values;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double
                })
                {
                    values = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (reader.Read())
                        throw new JsonReaderException("Unexpected content after the patch object.");
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
            {
                error = "patch is not valid JSON: " + exception.Message;
                return false;
            }

            if (values.Count == 0)
            {
                error = "patch must contain at least one supported preference.";
                return false;
            }

            patch = new SettingsPatch();
            foreach (JProperty property in values.Properties())
            {
                switch (property.Name)
                {
                    case "cameraStyle":
                        if (property.Value.Type != JTokenType.String
                            || !Enum.TryParse(property.Value.Value<string>(), out CameraStyle style)
                            || property.Value.Value<string>() != style.ToString()
                            || !Enum.IsDefined(typeof(CameraStyle), style))
                        {
                            error = "cameraStyle must be Manual, AutoCombat, Arena, AirDuel, SideDuel, or Shoulder.";
                            return false;
                        }
                        patch.CameraStyle = style;
                        break;
                    case "uiScale":
                        if (!TryReadNumber(property.Value, "uiScale", 80, 140, true, out double uiScale, out error)) return false;
                        if (uiScale is not (80 or 90 or 100 or 110 or 120 or 130 or 140))
                        {
                            error = "uiScale must be one of 80, 90, 100, 110, 120, 130, or 140.";
                            return false;
                        }
                        patch.UiScale = (int)uiScale;
                        break;
                    case "targetOpacity":
                        if (!TryReadNumber(property.Value, "targetOpacity", 20, 100, false, out double opacity, out error)) return false;
                        patch.TargetOpacity = (float)opacity;
                        break;
                    case "screenShake":
                        if (!TryReadNumber(property.Value, "screenShake", 0, 100, false, out double shake, out error)) return false;
                        patch.ScreenShake = (float)shake;
                        break;
                    case "showOverheadDamage":
                    case "reducedFlashing":
                    case "recordSessionLog":
                        if (property.Value.Type != JTokenType.Boolean)
                        {
                            error = property.Name + " must be a JSON boolean.";
                            return false;
                        }
                        if (property.Name == "showOverheadDamage") patch.ShowOverheadDamage = (bool)property.Value;
                        else if (property.Name == "reducedFlashing") patch.ReducedFlashing = (bool)property.Value;
                        else patch.RecordSessionLog = (bool)property.Value;
                        break;
                    default:
                        error = "Unsupported preference key: " + property.Name;
                        return false;
                }
            }
            return true;
        }

        private static bool TryReadNumber(JToken token, string name, double minimum, double maximum, bool integral,
            out double value, out string error)
        {
            value = 0;
            error = null;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                error = name + " must be a JSON number.";
                return false;
            }
            try { value = token.Value<double>(); }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is InvalidCastException)
            {
                error = name + " must be a finite JSON number.";
                return false;
            }
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum || (integral && value != Math.Truncate(value)))
            {
                error = name + (integral ? " must be an integer" : " must be") + " between " + minimum.ToString(CultureInfo.InvariantCulture) +
                    " and " + maximum.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }
            return true;
        }

        private static bool TryResolveOwner(out ClientSettingsService owner, out string error)
        {
            owner = null;
            error = null;
            var candidates = UnityEngine.Object.FindObjectsByType<ClientSettingsService>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var candidate in candidates)
            {
                Scene scene = candidate.gameObject.scene;
                if (candidate == null || !candidate.isActiveAndEnabled || !scene.IsValid() || !scene.isLoaded)
                    continue;
                if (owner != null)
                {
                    owner = null;
                    error = "Multiple active ClientSettingsService owners exist in loaded scenes; refusing an ambiguous settings target.";
                    return false;
                }
                owner = candidate;
            }
            if (owner == null)
                error = "No active ClientSettingsService owner exists in a loaded scene; settings commands do not create one.";
            return owner != null;
        }

        private static SlopArenaSettingsSnapshot ReadSnapshot(ClientSettingsService owner)
            => new SlopArenaSettingsSnapshot
            {
                UiScale = owner.UiScale,
                TargetOpacity = owner.TargetOpacity * 100f,
                ShowOverheadDamage = owner.ShowOverheadDamage,
                ReducedFlashing = owner.ReducedFlashing,
                ScreenShake = owner.ScreenShake * 100f,
                RecordSessionLog = owner.RecordSessionLog,
                CameraStyle = owner.CameraStyle.ToString()
            };

        private static SlopArenaSettingsPatchValues ToRequested(SettingsPatch patch)
            => new SlopArenaSettingsPatchValues
            {
                UiScale = patch.UiScale,
                TargetOpacity = patch.TargetOpacity,
                ShowOverheadDamage = patch.ShowOverheadDamage,
                ReducedFlashing = patch.ReducedFlashing,
                ScreenShake = patch.ScreenShake,
                RecordSessionLog = patch.RecordSessionLog,
                CameraStyle = patch.CameraStyle?.ToString()
            };

        private static SlopArenaSettingsSnapshot ApplyPatch(SlopArenaSettingsSnapshot before, SettingsPatch patch)
            => new SlopArenaSettingsSnapshot
            {
                UiScale = patch.UiScale ?? before.UiScale,
                TargetOpacity = patch.TargetOpacity.HasValue ? Mathf.Round(patch.TargetOpacity.Value) : before.TargetOpacity,
                ShowOverheadDamage = patch.ShowOverheadDamage ?? before.ShowOverheadDamage,
                ReducedFlashing = patch.ReducedFlashing ?? before.ReducedFlashing,
                ScreenShake = patch.ScreenShake.HasValue ? Mathf.Round(patch.ScreenShake.Value) : before.ScreenShake,
                RecordSessionLog = patch.RecordSessionLog ?? before.RecordSessionLog,
                CameraStyle = patch.CameraStyle?.ToString() ?? before.CameraStyle
            };

        private static SlopArenaSettingsCommandResult Failure(string message, SlopArenaSettingsPatchValues requested = null)
            => new SlopArenaSettingsCommandResult
            {
                Success = false,
                Requested = requested,
                PersistenceStatus = "not-written",
                Persisted = false,
                Diagnostics = new List<SlopArenaSettingsDiagnostic>
                {
                    new SlopArenaSettingsDiagnostic { Code = "settings_command_rejected", Message = message }
                }
            };

        internal sealed class SettingsPatch
        {
            internal int? UiScale;
            internal float? TargetOpacity;
            internal bool? ShowOverheadDamage;
            internal bool? ReducedFlashing;
            internal float? ScreenShake;
            internal bool? RecordSessionLog;
            internal CameraStyle? CameraStyle;
        }
    }

    public sealed class SlopArenaSettingsCommandResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("dryRun")] public bool DryRun { get; set; }
        [JsonProperty("persistenceStatus")] public string PersistenceStatus { get; set; }
        [JsonProperty("persisted")] public bool? Persisted { get; set; }
        [JsonProperty("requested")] public SlopArenaSettingsPatchValues Requested { get; set; }
        [JsonProperty("before")] public SlopArenaSettingsSnapshot Before { get; set; }
        [JsonProperty("proposed")] public SlopArenaSettingsSnapshot Proposed { get; set; }
        [JsonProperty("after")] public SlopArenaSettingsSnapshot After { get; set; }
        [JsonProperty("appliedFields")] public List<string> AppliedFields { get; set; } = new List<string>();
        [JsonProperty("diagnostics")] public List<SlopArenaSettingsDiagnostic> Diagnostics { get; set; } = new List<SlopArenaSettingsDiagnostic>();
    }

    public sealed class SlopArenaSettingsPatchValues
    {
        [JsonProperty("uiScale", NullValueHandling = NullValueHandling.Ignore)] public int? UiScale { get; set; }
        [JsonProperty("targetOpacity", NullValueHandling = NullValueHandling.Ignore)] public float? TargetOpacity { get; set; }
        [JsonProperty("showOverheadDamage", NullValueHandling = NullValueHandling.Ignore)] public bool? ShowOverheadDamage { get; set; }
        [JsonProperty("reducedFlashing", NullValueHandling = NullValueHandling.Ignore)] public bool? ReducedFlashing { get; set; }
        [JsonProperty("screenShake", NullValueHandling = NullValueHandling.Ignore)] public float? ScreenShake { get; set; }
        [JsonProperty("recordSessionLog", NullValueHandling = NullValueHandling.Ignore)] public bool? RecordSessionLog { get; set; }
        [JsonProperty("cameraStyle", NullValueHandling = NullValueHandling.Ignore)] public string CameraStyle { get; set; }
    }


    public sealed class SlopArenaSettingsSnapshot
    {
        [JsonProperty("uiScale")] public int UiScale { get; set; }
        [JsonProperty("targetOpacity")] public float TargetOpacity { get; set; }
        [JsonProperty("showOverheadDamage")] public bool ShowOverheadDamage { get; set; }
        [JsonProperty("reducedFlashing")] public bool ReducedFlashing { get; set; }
        [JsonProperty("screenShake")] public float ScreenShake { get; set; }
        [JsonProperty("recordSessionLog")] public bool RecordSessionLog { get; set; }
        [JsonProperty("cameraStyle")] public string CameraStyle { get; set; }
    }

    public sealed class SlopArenaSettingsDiagnostic
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }
}
