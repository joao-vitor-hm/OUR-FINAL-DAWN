#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// -batchmode -projectPath "..." -executeMethod V1Validation.Audit -logFile "audit.log"
/// -batchmode -projectPath "..." -executeMethod V1Validation.RunSmoke -logFile "smoke.log"
/// Optional: -v1Scene Assets/Scenes/Debug-Scene.unity. Do NOT pass -quit to RunSmoke.
/// Both commands exit the Editor: 0 = success, 1 = validation/runtime failure.
/// Never saves scenes or assets. Run in a dedicated Editor process.
/// </summary>
[InitializeOnLoad]
public static class V1Validation {
    private const string Key = "OurFinalDawn.V1Validation.Session";
    private const string Prefix = "[V1Validation] ";
    private static readonly float[] Durations = { 3f, 3f, 2f, 3f, 2f };
    private static readonly string[] Phases = { "settle", "forward", "stop", "right", "respawn" };
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags AllInstance = PublicInstance | BindingFlags.NonPublic;

    [Serializable]
    private sealed class Session {
        public bool smoke;
        public bool entered;
        public bool finishing;
        public bool initialized;
        public string scene;
        public string wallStart;
        public int phase;
        public int failures;
        public int samples;
        public int groundedSamples;
        public int forwardStartCompletedSteps;
        public double phaseStart;
        public Vector3 origin;
        public Quaternion rotation;
        public Vector3 phaseOrigin;
        public float rigSpan;
        public float speed;
        public float movingSpeed;
        public float maxTilt;
    }

    private static Session state;
    private static Player player;
    private static Rigidbody hips;
    private static ProceduralBalanceMotor motor;
    private static Rigidbody[] bodies;
    private static Collider[] colliders;
    private static MethodInfo setMovement;
    private static MethodInfo respawn;

    static V1Validation() {
        string saved = SessionState.GetString(Key, "");
        if (!string.IsNullOrEmpty(saved)) {
            state = JsonUtility.FromJson<Session>(saved);
            Subscribe();
        }
    }

    public static void Audit() { Begin(false); }
    public static void RunSmoke() { Begin(true); }

    private static void Begin(bool smoke) {
        if (state != null) {
            Fail("A validation session is already active.");
            Finish();
            return;
        }
        state = new Session {
            smoke = smoke,
            scene = Argument("-v1Scene", "Assets/Scenes/Debug-Scene.unity"),
            wallStart = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        Subscribe();
        Save();
        try {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start validation in Edit Mode.");
            if (smoke)
                Require(!HasArgument("-quit"), "RunSmoke is asynchronous; remove -quit. It exits the Editor itself.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "An open scene has unsaved edits; use a dedicated process.");
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(state.scene) != null, "Scene not found: " + state.scene);
            Scene scene = EditorSceneManager.OpenScene(state.scene, OpenSceneMode.Single);
            Log("AUDIT scene=" + scene.path);
            AuditScene(scene);
            if (!smoke || state.failures != 0) { Finish(); return; }
            BindPlayer(true);
            Log("SMOKE schedule: settle 3s, forward 3s, stop 2s, right 3s, Respawn 2s (simulation time).");
            Save();
            EditorApplication.isPlaying = true;
        } catch (Exception exception) {
            Fail(exception.ToString());
            Finish();
        }
    }

    private static void Subscribe() {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    private static void Save() {
        if (state != null) SessionState.SetString(Key, JsonUtility.ToJson(state));
    }

    private static void OnLog(string message, string stack, LogType type) {
        if (state == null || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        state.failures++;
        Save();
        // Log, not LogError: avoid recursively invoking this handler.
        Log("FAIL runtime " + type + ": " + message + "\n" + stack);
    }

    private static void OnPlayMode(PlayModeStateChange change) {
        if (state == null) return;
        if (change == PlayModeStateChange.EnteredPlayMode) {
            state.entered = true;
            Save();
        } else if (change == PlayModeStateChange.ExitingPlayMode && !state.finishing) {
            Fail("Play Mode ended before the smoke completed.");
            state.finishing = true;
            Save();
        } else if (change == PlayModeStateChange.EnteredEditMode) {
            if (!state.finishing) Fail("Returned to Edit Mode unexpectedly.");
            ExitEditor();
        }
    }

    private static void AuditScene(Scene scene) {
        int transforms = 0, bodyCount = 0, colliderCount = 0, jointCount = 0;
        foreach (GameObject root in scene.GetRootGameObjects()) {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true)) {
                transforms++;
                Check(Finite(transform.position) && Finite(transform.rotation), "Non-finite transform: " + Path(transform));
                foreach (Component component in transform.GetComponents<Component>()) {
                    if (component == null) { Fail("Missing script: " + Path(transform)); continue; }
                    if (component is Rigidbody) bodyCount++;
                    if (component is Collider) colliderCount++;
                    if (component is Joint) jointCount++;
                    // A null optional reference is valid; a serialized dangling ID is not.
                    using (var serialized = new SerializedObject(component)) {
                        SerializedProperty property = serialized.GetIterator();
                        while (property.Next(true)) {
                            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                                Fail("Missing reference: " + Path(transform) + "/" + component.GetType().Name + "." + property.propertyPath);
                        }
                    }
                }
            }
        }
        Log($"SCENE transforms={transforms} bodies={bodyCount} colliders={colliderCount} joints={jointCount}");
        BindPlayer(false);
        Animator animator = player.GetComponentInChildren<Animator>(true);
        Require(animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman,
            "Player needs a valid humanoid Animator/avatar.");
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) {
            Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
            if (HumanTrait.RequiredBone(i)) Check(bone != null, "Missing required humanoid bone: " + (HumanBodyBones)i);
            if (bone != null) Log($"BONE {(HumanBodyBones)i} world={bone.position.ToString("F4")} local={bone.localPosition.ToString("F4")}");
        }
        Check(animator.GetBoneTransform(HumanBodyBones.Hips) == hips.transform, "Player Hips does not match humanoid Hips.");
        Check(bodies.Length > 1 && colliders.Length > 0, "Active ragdoll needs multiple bodies and colliders.");
        float span = RigSpan();
        Joint[] joints = player.GetComponentsInChildren<Joint>(true);
        Check(joints.Length > 0, "Player has no ragdoll joints.");
        foreach (Rigidbody body in bodies) {
            Log($"BODY {Path(body.transform)} mass={body.mass:F3} kinematic={body.isKinematic} position={body.position.ToString("F4")}");
            Check(Finite(body.mass) && body.mass > 0, "Invalid mass: " + body.name);
        }
        foreach (Collider collider in colliders)
            Log($"COLLIDER {Path(collider.transform)} type={collider.GetType().Name} enabled={collider.enabled} trigger={collider.isTrigger} body={(collider.attachedRigidbody == null ? "none" : collider.attachedRigidbody.name)}");
        foreach (Joint joint in joints) {
            Check(joint.connectedBody != null, "Unconnected ragdoll joint: " + Path(joint.transform));
            Vector3 anchor = joint.transform.TransformPoint(joint.anchor);
            Vector3 connected = joint.connectedBody != null
                ? joint.connectedBody.transform.TransformPoint(joint.connectedAnchor) : joint.connectedAnchor;
            float gap = Vector3.Distance(anchor, connected);
            Log($"JOINT {Path(joint.transform)} connected={(joint.connectedBody == null ? "WORLD" : Path(joint.connectedBody.transform))} anchor={joint.anchor.ToString("F4")} connectedAnchor={joint.connectedAnchor.ToString("F4")} worldGap={gap:F5}");
            Check(Finite(anchor) && Finite(connected) && gap <= span, "Joint anchor separation exceeds rig size: " + joint.name);
            if (gap > Physics.defaultContactOffset) Log("WARN joint anchor gap exceeds contact offset: " + joint.name);
        }
        Log($"PLAYER bodies={bodies.Length} colliders={colliders.Length} joints={joints.Length} rigSpan={span:F3}");
    }

    private static void BindPlayer(bool requireSmokeApi) {
        Player[] players = UnityEngine.Object.FindObjectsByType<Player>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(players.Length == 1, "Expected exactly one active Player; found " + players.Length);
        player = players[0];
        // Reflection allows this editor-only file to compile while the public API is added in parallel.
        hips = Read(player, "Hips", "m_Hips") as Rigidbody;
        motor = player.GetComponent<ProceduralBalanceMotor>();
        Require(hips != null, "Player Hips reference is missing.");
        Require(motor != null, "Player is missing ProceduralBalanceMotor.");
        bodies = player.GetComponentsInChildren<Rigidbody>(true);
        colliders = player.GetComponentsInChildren<Collider>(true);
        if (!requireSmokeApi) return;
        setMovement = player.GetType().GetMethod("SetDebugMovement", PublicInstance, null, new[] { typeof(Vector3?) }, null);
        respawn = player.GetType().GetMethod("Respawn", PublicInstance, null, Type.EmptyTypes, null);
        Require(setMovement != null, "Missing public Player.SetDebugMovement(Vector3? direction).");
        Require(respawn != null, "Missing public Player.Respawn().");
        Require(Read(motor, "HasGround") is bool, "Missing motor HasGround boolean.");
        Require(player.enabled && motor.enabled && !hips.isKinematic, "Player/motor must be enabled and Hips dynamic.");
    }

    private static void Tick() {
        if (state == null) return;
        try {
            double elapsed = (DateTime.UtcNow - DateTime.Parse(state.wallStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).TotalSeconds;
            if (elapsed > 180) {
                Fail("180s wall-clock watchdog expired (play transition, pause or stalled simulation).");
                ExitEditor();
                return;
            }
            if (state.finishing) {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitEditor();
                return;
            }
            if (!state.smoke || !state.entered || !EditorApplication.isPlaying) return;
            if (state.failures != 0) { Finish(); return; }
            if (player == null) BindPlayer(true);
            if (!state.initialized) {
                Require(Time.timeScale > 0 && Time.fixedDeltaTime > 0, "Simulation must advance.");
                Require(Physics.simulationMode == SimulationMode.FixedUpdate, "Smoke requires automatic FixedUpdate physics.");
                state.origin = hips.position;
                state.rotation = hips.rotation;
                state.rigSpan = RigSpan();
                state.speed = Mathf.Max(Number(player, "Speed"), Number(motor, "Speed"));
                state.initialized = true;
                StartPhase(0);
            }
            Sample();
            if (state.failures != 0) { Finish(); return; }
            if (Time.timeAsDouble - state.phaseStart < Durations[state.phase]) {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            EndPhase();
            if (state.failures != 0 || state.phase == Durations.Length - 1) { Finish(); return; }
            StartPhase(state.phase + 1);
        } catch (Exception exception) {
            Fail(exception.ToString());
            Finish();
        }
    }

    private static void StartPhase(int phase) {
        state.phase = phase;
        state.phaseStart = Time.timeAsDouble;
        state.phaseOrigin = hips.position;
        if (phase == 1) {
            ActiveRagdollMuscle muscle = player.GetComponent<ActiveRagdollMuscle>();
            Require(muscle != null, "Player is missing ActiveRagdollMuscle.");
            object completedSteps = Read(muscle, "CompletedSteps");
            Require(completedSteps is int, "Missing ActiveRagdollMuscle.CompletedSteps integer property.");
            state.forwardStartCompletedSteps = (int)completedSteps;
        }
        Vector3 direction = phase == 1 ? Vector3.forward : phase == 3 ? Vector3.right : Vector3.zero;
        setMovement.Invoke(player, new object[] { (Vector3?)direction });
        if (phase == 4) respawn.Invoke(player, null);
        Log("PHASE " + Phases[phase] + " start=" + hips.position.ToString("F4"));
        Save();
    }

    private static void Sample() {
        Require(player != null && hips != null && motor != null, "Player, Hips or motor destroyed during smoke.");
        Require(player.enabled && motor.enabled && !hips.isKinematic, "Player/motor disabled or Hips became kinematic.");
        state.samples++;
        if ((bool)Read(motor, "HasGround")) state.groundedSamples++;
        // Bound total travel by configured speed over the entire 13-second sequence plus rig size.
        float travelLimit = state.speed * 13f + state.rigSpan * 2f;
        Check(Finite(hips.position) && Vector3.Distance(hips.position, state.origin) <= travelLimit,
            "Hips left the configured travel envelope.");
        foreach (Rigidbody body in bodies) {
            Require(body != null, "A ragdoll body was destroyed.");
            Check(Finite(body.position) && Finite(body.rotation) && Finite(body.linearVelocity) && Finite(body.angularVelocity),
                "Non-finite rigidbody state: " + body.name);
            Check(Vector3.Distance(body.position, hips.position) <= state.rigSpan * 2f,
                "Ragdoll body separated by more than twice initial rig size: " + body.name);
        }
        foreach (Collider collider in colliders) {
            Require(collider != null, "A ragdoll collider was destroyed.");
            Check(Finite(collider.bounds.center) && Finite(collider.bounds.extents), "Non-finite collider bounds: " + collider.name);
        }
        // Delta maps the initial world pose onto the current one: yaw does not count as tilt.
        // The anatomical bone's local up axis is deliberately never used.
        Quaternion delta = hips.rotation * Quaternion.Inverse(state.rotation);
        float tilt = Vector3.Angle(Vector3.up, delta * Vector3.up);
        state.maxTilt = Mathf.Max(state.maxTilt, tilt);
        Check(Finite(tilt) && tilt < 90f, "Hips tipped beyond the upright hemisphere relative to their initial pose.");
    }

    private static void EndPhase() {
        Vector3 displacement = hips.position - state.phaseOrigin;
        float planarSpeed = Vector3.ProjectOnPlane(hips.linearVelocity, Vector3.up).magnitude;
        float contactSkin = Physics.defaultContactOffset;
        Log($"PHASE {Phases[state.phase]} end={hips.position.ToString("F4")} delta={displacement.ToString("F4")} planarSpeed={planarSpeed:F4} grounded={Read(motor, "HasGround")} maxTilt={state.maxTilt:F2} rotationDelta={Quaternion.Angle(state.rotation, hips.rotation):F2}");
        ActiveRagdollMuscle muscle = player.GetComponent<ActiveRagdollMuscle>();
        Require(muscle != null, "Player is missing ActiveRagdollMuscle.");
        object completedSteps = Read(muscle, "CompletedSteps");
        object footTargetError = Read(muscle, "FootTargetError");
        Require(completedSteps is int && footTargetError is float, "Missing ActiveRagdollMuscle.CompletedSteps or FootTargetError properties.");
        Log($"GAIT {Phases[state.phase]} CompletedSteps={(int)completedSteps} FootTargetError={(float)footTargetError:F4}");
        if (state.phase == 1) {
            int stepProgress = (int)completedSteps - state.forwardStartCompletedSteps;
            Log($"GAIT forward completedStepsStart={state.forwardStartCompletedSteps} completedStepsDelta={stepProgress}");
            Check(stepProgress > 0, "Forward phase completed no new gait steps; steps from settling do not count.");
        }
        // Movement can end just past the ramp edge; settling must recover ground support.
        if (state.phase == 0 || state.phase == 2 || state.phase == 4) {
            Check((bool)Read(motor, "HasGround"), "No ground after settling in " + Phases[state.phase]);
        }
        if (state.phase == 1 || state.phase == 3) {
            Vector3 direction = state.phase == 1 ? Vector3.forward : Vector3.right;
            Check(Vector3.Dot(displacement, direction) > contactSkin, "No measurable progress along commanded " + Phases[state.phase] + " direction.");
            if (state.phase == 1) state.movingSpeed = planarSpeed;
        }
        if (state.phase == 2) {
            // Rigidbody.sleepThreshold is mass-normalized kinetic energy: sqrt(2E) is
            // the corresponding linear rest speed, a physics-defined stopping tolerance.
            float restSpeed = Mathf.Sqrt(2f * hips.sleepThreshold) + contactSkin / Durations[2];
            Check(planarSpeed <= restSpeed, $"Stop phase did not settle: speed={planarSpeed:F4}, restTolerance={restSpeed:F4}, movingSpeed={state.movingSpeed:F4}.");
        }
        if (state.phase == 4) {
            // Respawn can settle vertically; horizontal return should be within the hips collider radius.
            float radius = contactSkin;
            foreach (Collider collider in colliders)
                if (collider.attachedRigidbody == hips) radius = Mathf.Max(radius, collider.bounds.extents.magnitude);
            float resetDistance = Vector3.ProjectOnPlane(hips.position - state.origin, Vector3.up).magnitude;
            Check(resetDistance <= radius + state.speed * Time.fixedDeltaTime, "Respawn did not return Hips to the spawn area.");
        }
        object ground = Read(motor, "GroundPoint");
        object standing = Read(motor, "StandingHeight");
        if (ground is Vector3 point && standing is float standingHeight) {
            float actualHeight = hips.worldCenterOfMass.y - point.y;
            Log($"HEIGHT actual={actualHeight:F4} configured={standingHeight:F4}");
            Check(Finite(actualHeight) && Finite(standingHeight) && standingHeight > 0 && actualHeight > 0 && actualHeight <= state.rigSpan,
                "Hips standing height is outside the ground-to-rig-size range.");
        }
        Save();
    }

    private static float RigSpan() {
        Bounds bounds = new Bounds(hips.position, Vector3.zero);
        foreach (Rigidbody body in bodies) bounds.Encapsulate(body.position);
        foreach (Collider collider in colliders)
            if (collider.enabled && collider.gameObject.activeInHierarchy) bounds.Encapsulate(collider.bounds);
        float span = bounds.size.magnitude;
        Require(Finite(span) && span > Physics.defaultContactOffset, "Ragdoll has no finite physical extent.");
        return span;
    }

    private static object Read(object target, params string[] names) {
        foreach (string name in names) {
            PropertyInfo property = target.GetType().GetProperty(name, AllInstance);
            if (property != null) return property.GetValue(target);
            FieldInfo field = target.GetType().GetField(name, AllInstance);
            if (field != null) return field.GetValue(target);
        }
        return null;
    }

    private static float Number(object target, string name) {
        object value = Read(target, name);
        return value is float number && Finite(number) ? Mathf.Max(0, number) : 0;
    }

    private static void Finish() {
        if (state == null || state.finishing) return;
        state.finishing = true;
        Save();
        if (EditorApplication.isPlaying && player != null && setMovement != null) {
            try { setMovement.Invoke(player, new object[] { null }); }
            catch (Exception exception) { Fail("Could not release debug movement: " + exception.Message); }
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else ExitEditor();
    }

    private static void ExitEditor() {
        if (state == null) return;
        int code = state.failures == 0 ? 0 : 1;
        Log($"RESULT {(state.smoke ? "SMOKE" : "AUDIT")} {(code == 0 ? "PASS" : "FAIL")} failures={state.failures} samples={state.samples} groundedSamples={state.groundedSamples} maxTilt={state.maxTilt:F2} exitCode={code}");
        SessionState.EraseString(Key);
        state = null;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        Application.logMessageReceived -= OnLog;
        // Application.Quit does not terminate the Editor; this is its command-line equivalent.
        EditorApplication.Exit(code);
    }

    private static void Check(bool condition, string message) { if (!condition) Fail(message); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(string message) { state.failures++; Save(); Log("FAIL " + message); }
    private static void Log(string message) { Debug.Log(Prefix + message); }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
    private static bool Finite(Quaternion value) { return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w); }
    private static string Path(Transform transform) { return transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name; }
    private static bool HasArgument(string name) { return Array.Exists(Environment.GetCommandLineArgs(), item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)); }
    private static string Argument(string name, string fallback) {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase)) return arguments[i + 1];
        return fallback;
    }
}
#endif
