using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FurtherDevelopment.SocialAI
{
    public sealed class SocialAiPlayground : MonoBehaviour
    {
        [SerializeField] private SocialScenarioAsset scenario = null;
        private FactionAttackRule activeRule;
        private string victimId;
        private string witnessId;
        private string responderId;
        private Vector3 responderHome;
        private float witnessRadius;
        private float moveSpeed;
        private string configurationError;
        [SerializeField] private Shader pixelShader = null;
        [SerializeField, Range(2, 8)] private int pixelScale = 4;
        [SerializeField, Range(6, 20)] private float cameraSize = 10;
        [SerializeField, Range(45, 90)] private float cameraPitch = 60;
        private RenderTexture pixelFrame;
        private Rect gameView;
        private readonly Dictionary<string, Transform> actors = new Dictionary<string, Transform>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<string> timeline = new List<string>();
        private readonly List<TextMesh> labels = new List<TextMesh>();
        private SocialWorld world;
        private Camera viewCamera;
        private bool running;
        private bool awaitingReply;
        private bool attackRecorded;
        private string reply;
        private Vector2 scroll;
        private string phase = "Ready";

        private void Awake()
        {
            CreateBody("Ground", PrimitiveType.Plane, Vector3.zero, new Vector3(8, 1, 8), new Color(0.14f, 0.19f, 0.19f));
            var cameraObject = new GameObject("Playground Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform);
            viewCamera = cameraObject.GetComponent<Camera>();
            viewCamera.transform.position = new Vector3(4, 28, -27);
            viewCamera.transform.LookAt(new Vector3(4, 0, 1));
            viewCamera.orthographic = true;
            viewCamera.allowMSAA = false;
            viewCamera.allowHDR = false;
            viewCamera.orthographicSize = 17;
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = new Color(0.09f, 0.12f, 0.15f);
            // The world camera renders to a texture. Keep a camera targeting Display 1
            // so the Game view has an active display renderer beneath the GUI composite.
            var displayObject = new GameObject("Display Camera", typeof(Camera));
            displayObject.transform.SetParent(transform);
            var displayCamera = displayObject.GetComponent<Camera>();
            displayCamera.cullingMask = 0;
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            displayCamera.backgroundColor = Color.black;
            displayCamera.depth = -100;
            displayCamera.targetDisplay = 0;
            displayCamera.allowHDR = false;
            displayCamera.allowMSAA = false;
            var sunObject = new GameObject("Pixel Sun", typeof(Light));
            sunObject.transform.SetParent(transform);
            sunObject.transform.rotation = Quaternion.Euler(50, -35, 0);
            var sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1, 0.94f, 0.82f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.9f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.2f;
            ResetDemo();
        }

        private GameObject CreateBody(string objectName, PrimitiveType shape, Vector3 position, Vector3 scale, Color color)
        {
            var body = GameObject.CreatePrimitive(shape);
            body.name = objectName;
            body.transform.SetParent(transform);
            body.transform.position = position;
            body.transform.localScale = scale;
            var shader = pixelShader != null ? pixelShader : Shader.Find("FurtherDevelopment/PixelPalette");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Ground")) material.SetFloat("_Ground", shape == PrimitiveType.Plane ? 1 : 0);
            body.GetComponent<Renderer>().sharedMaterial = material;
            materials.Add(material);
            return body;
        }

        private void AddActor(string id, string displayName, Color color)
        {
            var actor = CreateBody(displayName, PrimitiveType.Capsule, Vector3.zero, Vector3.one, color);
            actors.Add(id, actor.transform);
            var title = new GameObject("Name", typeof(TextMesh));
            title.transform.SetParent(actor.transform, false);
            title.transform.localPosition = new Vector3(0, 1.8f, 0);
            var label = title.GetComponent<TextMesh>();
            label.text = displayName;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = 0.09f;
            label.color = Color.white;
            // Draw name labels at native resolution above the pixelated world.
            title.GetComponent<MeshRenderer>().enabled = false;
            labels.Add(label);
        }

        private void LateUpdate()
        {
            if (world == null) return;
            float panelWidth = Mathf.Min(390, Screen.width * 0.42f) + 24;
            int scale = Mathf.Max(2, pixelScale);
            int width = Mathf.Max(1, Mathf.FloorToInt((Screen.width - panelWidth) / scale));
            int height = Mathf.Max(1, Screen.height / scale);
            // Integer upscaling gives every world pixel the same square footprint.
            gameView = new Rect(Mathf.Floor(panelWidth + (Screen.width - panelWidth - width * scale) * 0.5f),
                Mathf.Floor((Screen.height - height * scale) * 0.5f), width * scale, height * scale);
            if (pixelFrame == null || pixelFrame.width != width || pixelFrame.height != height)
            {
                ReleasePixelFrame();
                pixelFrame = new RenderTexture(width, height, 24)
                {
                    name = "Social AI Pixel View", filterMode = FilterMode.Point,
                    antiAliasing = 1, useMipMap = false, wrapMode = TextureWrapMode.Clamp
                };
                pixelFrame.Create();
                viewCamera.targetTexture = pixelFrame;
            }
            viewCamera.aspect = gameView.width / gameView.height;
            viewCamera.orthographicSize = cameraSize;
            viewCamera.transform.rotation = Quaternion.Euler(cameraPitch, 0, 0);
            viewCamera.transform.position = actors["player"].position - viewCamera.transform.forward * 30;
        }

        public void ResetDemo()
        {
            StopAllCoroutines();
            running = false;
            awaitingReply = false;
            attackRecorded = false;
            reply = null;
            phase = "Ready";
            timeline.Clear();
            world = null;
            configurationError = null;
            if (scenario == null || !scenario.TryValidate(out configurationError))
            {
                configurationError = configurationError ?? "Assign a Scenario asset in the Inspector.";
                Debug.LogError(configurationError, this);
                return;
            }
            foreach (var actor in actors.Values)
            {
                var material = actor.GetComponent<Renderer>().sharedMaterial;
                materials.Remove(material);
                Destroy(material);
                actor.gameObject.SetActive(false);
                Destroy(actor.gameObject);
            }
            actors.Clear();
            labels.Clear();
            var profiles = new List<NpcProfile>();
            foreach (var spawn in scenario.GetSpawns())
            {
                profiles.Add(spawn.profile.CreateProfile());
                AddActor(spawn.profile.id, spawn.profile.displayName, spawn.profile.color);
                actors[spawn.profile.id].position = spawn.position;
            }
            AddActor("player", "Player", scenario.playerColor);
            actors["player"].position = scenario.playerPosition;
            victimId = scenario.victim.profile.id;
            witnessId = scenario.witness.profile.id;
            responderId = scenario.responder.profile.id;
            responderHome = scenario.responder.position;
            witnessRadius = scenario.witnessRadius;
            moveSpeed = scenario.npcMoveSpeed;
            activeRule = scenario.socialRule.CreateRule();
            world = new SocialWorld("player", profiles);
            scenario.initialRelationships.ApplyTo(world);
            Log("Loaded " + scenario.name + ". Inspector changes apply on Reset.");
            Log("WASD / arrows to move. Approach " + actors[victimId].name + " and press Space.");
        }

        public void RunDemo()
        {
            ResetDemo();
        }

        private void Update()
        {
            if (world == null || awaitingReply) return;
            Vector2 input = Vector2.zero;
            bool attackPressed = false;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            input.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
            input.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
            attackPressed = keyboard.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            attackPressed = Input.GetKeyDown(KeyCode.Space);
#endif
            var player = actors["player"];
            var movement = Vector2.ClampMagnitude(input, 1) * (5 * Time.deltaTime);
            var destination = player.position + new Vector3(movement.x, 0, movement.y);
            destination.x = Mathf.Clamp(destination.x, -38, 38);
            destination.z = Mathf.Clamp(destination.z, -38, 38);
            bool blocked = false;
            foreach (var actor in actors)
                if (actor.Key != "player" && Vector3.Distance(destination, actor.Value.position) < 1.05f)
                    blocked = true;
            if (!blocked) player.position = destination;
            if (attackPressed) TryAttackAlex();
        }

        public void TryAttackAlex()
        {
            if (world == null || attackRecorded || running) return;
            if (Vector3.Distance(actors["player"].position, actors[victimId].position) > 2.3f)
            {
                phase = $"Move within 2.3 metres of {actors[victimId].name} to attack";
                return;
            }
            attackRecorded = true;
            running = true;
            StartCoroutine(PlaySequence());
        }

        private IEnumerator PlaySequence()
        {
            var rule = activeRule;
            phase = $"Player attacks {actors[victimId].name}";
            var player = actors["player"];
            var witnesses = new Dictionary<string, bool>();
            foreach (var id in new[] { witnessId, responderId })
                if (Vector3.Distance(actors[id].position, actors[victimId].position) <= witnessRadius)
                    witnesses.Add(id, true);
            var attack = world.RecordAttack("player", victimId, witnesses);
            Log($"Attack recorded. {actors[victimId].name} experienced it. Witnesses: " + string.Join(", ", witnesses.Keys));
            Log($"{actors[responderId].name} before report: " + rule.Evaluate(world, responderId, "player", attack.Id).Action);
            actors[victimId].localScale = new Vector3(1.2f, 0.8f, 1.2f);
            yield return new WaitForSeconds(0.25f);
            actors[victimId].localScale = Vector3.one;
            yield return new WaitForSeconds(1.5f);

            if (witnesses.ContainsKey(witnessId))
            {
                phase = $"{actors[witnessId].name} approaches {actors[responderId].name} to report";
                yield return MoveTo(actors[witnessId], actors[responderId].position + Vector3.left * 2);
                bool inRange = Vector3.Distance(actors[witnessId].position, actors[responderId].position) <= 2.5f;
                if (!world.TryReport(attack.Id, witnessId, responderId, inRange, out var report))
                {
                    Log("Report failed: " + report);
                    phase = "Report failed";
                    running = false;
                    yield break;
                }
                Log($"{actors[witnessId].name}: I saw the player hit {actors[victimId].name}. I only saw the end.");
                Log(report);
                yield return new WaitForSeconds(2);
            }

            var initialDecision = rule.Evaluate(world, responderId, "player", attack.Id);
            if (initialDecision.Action == SocialAction.ContinuePatrol)
            {
                Log(initialDecision.ToString());
                running = false;
                phase = "No reaction - Reset to try another configuration";
                yield break;
            }

            phase = $"{actors[responderId].name} encounters the player";
            yield return Approach(actors[responderId], player, 2.5f);
            Log(witnesses.ContainsKey(responderId)
                ? $"{actors[responderId].name}: I saw you hit {actors[victimId].name}. What happened?"
                : $"{actors[responderId].name}: {actors[witnessId].name} says you hit {actors[victimId].name}. What happened?");
            phase = $"Choose your response to {actors[responderId].name}";
            awaitingReply = true;
            yield return new WaitUntil(() => reply != null);
            VerificationRequest request = null;
            if (reply == "verify")
            {
                Log($"Player: I hit {actors[victimId].name}. Ask them what happened before {actors[witnessId].name} arrived.");
                request = new VerificationRequest(attack.Id, "player", victimId, "What happened before the player hit you?");
            }
            else Log(reply == "deny" ? $"Player: I did not hit {actors[victimId].name}." : $"Player: Yes, I hit {actors[victimId].name}.");
            var decision = rule.Evaluate(world, responderId, "player", attack.Id, request);
            Log("Requested: " + decision);
            yield return new WaitForSeconds(1.5f);

            phase = decision.Action.ToString();
            if (decision.Action == SocialAction.ContinuePatrol)
            {
                Log("Started: return to patrol point.");
                yield return MoveTo(actors[responderId], responderHome);
                Log($"Completed: {actors[responderId].name} returns to patrol.");
            }
            else if (decision.ActorId == responderId && actors.TryGetValue(decision.TargetId, out var target))
            {
                Log("Started: " + decision.Action + " with " + target.name);
                yield return Approach(actors[responderId], target, 1.8f);
                Log($"Completed: {actors[responderId].name} reached " + target.name);
                Log(decision.Action == SocialAction.VerifyWith
                    ? $"{actors[responderId].name} to {actors[victimId].name}: " + decision.Question
                    : reply == "admit" ? $"{actors[responderId].name}: You admitted hitting {actors[victimId].name}. You need to answer for it."
                    : $"{actors[responderId].name}: {actors[witnessId].name} saw you hit {actors[victimId].name}. I am confronting you about it.");
            }
            else Log("Action rejected: invalid actor or target.");
            running = false;
            awaitingReply = false;
            phase = "Complete - Reset to try another response";
        }

        private IEnumerator Approach(Transform actor, Transform target, float distance)
        {
            while (Vector3.Distance(actor.position, target.position) > distance + 0.02f)
            {
                actor.position = Vector3.MoveTowards(actor.position, target.position,
                    Mathf.Min(Mathf.Max(0.1f, moveSpeed) * Time.deltaTime,
                        Vector3.Distance(actor.position, target.position) - distance));
                yield return null;
            }
        }

        private IEnumerator MoveTo(Transform actor, Vector3 destination)
        {
            while (Vector3.Distance(actor.position, destination) > 0.02f)
            {
                actor.position = Vector3.MoveTowards(actor.position, destination, Mathf.Max(0.1f, moveSpeed) * Time.deltaTime);
                yield return null;
            }
            actor.position = destination;
        }

        private void Log(string entry)
        {
            timeline.Add(entry);
            scroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
            if (pixelFrame != null)
            {
                GUI.DrawTexture(gameView, pixelFrame, ScaleMode.StretchToFill, false);
                var nameStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold
                };
                foreach (var label in labels)
                {
                    var point = viewCamera.WorldToViewportPoint(label.transform.position);
                    if (point.z <= 0 || point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1) continue;
                    float x = gameView.x + point.x * gameView.width;
                    float y = gameView.y + (1 - point.y) * gameView.height;
                    GUI.Box(new Rect(x - 44, y - 12, 88, 24), GUIContent.none);
                    GUI.Label(new Rect(x - 44, y - 12, 88, 24), label.text, nameStyle);
                }
            }
            float width = Mathf.Min(390, Screen.width * 0.42f);
            GUILayout.BeginArea(new Rect(12, 12, width, Screen.height - 24), GUI.skin.box);
            GUILayout.Label("Social AI Playground");
            if (GUILayout.Button("Reset / Reload configuration")) ResetDemo();
            if (world == null)
            {
                GUILayout.Label(configurationError ?? "Configuration is unavailable.");
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label("Rule enabled: " + activeRule.Enabled + " | Verification: " + activeRule.AllowVerification);
            GUILayout.Label("Report confidence: " + world.GetTrust(responderId, witnessId) + " / " + activeRule.MinimumConfidence);
            GUILayout.Label($"WASD / arrows: move | Space: attack {actors[victimId].name}");
            GUI.enabled = !attackRecorded;
            if (GUILayout.Button($"Attack {actors[victimId].name} (within 2.3m)")) TryAttackAlex();
            GUI.enabled = true;
            if (awaitingReply && reply == null)
            {
                GUILayout.Label($"{actors[responderId].name} is waiting. Choose a response:");
                if (GUILayout.Button($"Deny: I did not hit {actors[victimId].name}.")) reply = "deny";
                if (GUILayout.Button($"Admit: Yes, I hit {actors[victimId].name}.")) reply = "admit";
                if (GUILayout.Button($"Ask {actors[victimId].name} what happened before {actors[witnessId].name} arrived.")) reply = "verify";
            }
            GUILayout.Label("State: " + phase);
            foreach (var id in new[] { victimId, witnessId, responderId })
            {
                var knowledge = world.GetNpc(id).Knowledge;
                GUILayout.Label(actors[id].name + ": " + (knowledge.Count == 0 ? "Unaware" : knowledge[0].Acquisition + " from " + knowledge[0].SourceId));
            }
            scroll = GUILayout.BeginScrollView(scroll);
            var wrappedLabel = new GUIStyle(GUI.skin.label) { wordWrap = true };
            foreach (var entry in timeline) GUILayout.Label(entry, wrappedLabel);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            ReleasePixelFrame();
            foreach (var material in materials) Destroy(material);
        }

        private void ReleasePixelFrame()
        {
            if (pixelFrame == null) return;
            if (viewCamera != null) viewCamera.targetTexture = null;
            pixelFrame.Release();
            Destroy(pixelFrame);
            pixelFrame = null;
        }
    }
}


