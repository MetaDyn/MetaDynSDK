using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GLTFast;
using MetaDyn.Networking;
using Starter;
using UnityEngine;
using UnityEngine.UI;

namespace MetaDyn.Avatar
{
/// <summary>
    /// Carica un .glb custom dall'utente e lo applica come avatar al player locale.
    /// Le ossa del .glb devono seguire la naming convention RPM/Unity standard
    /// (es. Hips, Spine, LeftUpperArm, RightUpperLeg, ecc.). Supportati anche rig
    /// Mixamo con namespace ("mixamorig:Hips" → matchato come "Hips").
    ///
    /// Setup: aggiungere questo component a qualsiasi GameObject nella scena.
    /// Se uploadButton è null, crea automaticamente un bottone overlay in basso a destra.
    /// </summary>
    public class GLBAvatarLoader : MonoBehaviour
    {
        [SerializeField] private Button uploadButton;

        // Modello GLB attualmente applicato (distrutto prima di applicarne uno nuovo,
        // per evitare nomi di ossa duplicati nella gerarchia → binding ambiguo → T-pose)
        private GameObject _currentAvatarModel;
        private byte[] _pendingAvatarData;
        private UIGameMenu _gameMenu;

        private const string GltfMetallicMaterialPath = "GLTFastShaders/gltfast_metallic";
        private const string GltfSpecularMaterialPath = "GLTFastShaders/gltfast_specular";
        private const string GltfUnlitMaterialPath = "GLTFastShaders/gltfast_unlit";
        private const int MaterialLogLimit = 16;

        // Mapping humanoid → alias accettati (supporta RPM standard e Mixamo)
        // Primo alias trovato nel GLB viene usato come boneName
        private static readonly (string humanName, string[] aliases, bool required)[] BoneMap =
        {
            ("Hips",         new[] { "Hips" },                          true),
            ("Spine",        new[] { "Spine" },                         true),
            ("Chest",        new[] { "Chest",        "Spine1" },        false),
            ("UpperChest",   new[] { "UpperChest",   "Spine2" },        false),
            ("Neck",         new[] { "Neck" },                          false),
            ("Head",         new[] { "Head" },                          true),
            ("LeftShoulder", new[] { "LeftShoulder" },                  false),
            ("LeftUpperArm", new[] { "LeftUpperArm", "LeftArm" },       true),
            ("LeftLowerArm", new[] { "LeftLowerArm", "LeftForeArm" },   false),
            ("LeftHand",     new[] { "LeftHand" },                      false),
            ("RightShoulder",new[] { "RightShoulder" },                 false),
            ("RightUpperArm",new[] { "RightUpperArm","RightArm" },      true),
            ("RightLowerArm",new[] { "RightLowerArm","RightForeArm" },  false),
            ("RightHand",    new[] { "RightHand" },                     false),
            ("LeftUpperLeg", new[] { "LeftUpperLeg", "LeftUpLeg" },     true),
            ("LeftLowerLeg", new[] { "LeftLowerLeg", "LeftLeg" },       false),
            ("LeftFoot",     new[] { "LeftFoot" },                      false),
            ("LeftToes",     new[] { "LeftToes",     "LeftToeBase" },   false),
            ("RightUpperLeg",new[] { "RightUpperLeg","RightUpLeg" },    true),
            ("RightLowerLeg",new[] { "RightLowerLeg","RightLeg" },      false),
            ("RightFoot",    new[] { "RightFoot" },                     false),
            ("RightToes",    new[] { "RightToes",    "RightToeBase" },  false),
        };

        private void Awake()
        {
            _gameMenu = GetComponent<UIGameMenu>();
        }

        private void OnEnable()
        {
            MetaDynUGSPlayerController.OnLocalPlayerReady += HandleLocalPlayerReady;
        }

        private void OnDisable()
        {
            MetaDynUGSPlayerController.OnLocalPlayerReady -= HandleLocalPlayerReady;
        }

        private void Start()
        {
            if (uploadButton == null)
                uploadButton = CreateOverlayButton();

            uploadButton.onClick.AddListener(OpenFilePicker);
        }

        private void HandleLocalPlayerReady()
        {
            if (_pendingAvatarData != null)
            {
                Debug.Log("[GLBAvatarLoader] Applying pending custom avatar to newly spawned player.");
                _ = LoadAndApply(_pendingAvatarData);
                _pendingAvatarData = null;
            }
        }

        // Crea un bottone overlay a schermo intero, posizionato in basso a destra
        private Button CreateOverlayButton()
        {
            // Canvas dedicato (Screen Space - Overlay)
            var canvasGO = new GameObject("GLBAvatarLoader_Canvas");
            canvasGO.transform.SetParent(transform);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Bottone quadrato 64x64
            var btnGO = new GameObject("UploadAvatarButton");
            btnGO.transform.SetParent(canvasGO.transform, false);

            var rect = btnGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot     = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-20f, 20f);
            rect.sizeDelta = new Vector2(64f, 64f);

            var img = btnGO.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

            var btn = btnGO.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            colors.pressedColor     = new Color(0.05f, 0.05f, 0.05f, 1f);
            btn.colors = colors;

            // Label "GLB"
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(btnGO.transform, false);
            var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGO.AddComponent<Text>();
            text.text      = "GLB";
            text.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize  = 14;
            text.fontStyle = FontStyle.Bold;
            text.color     = Color.white;
            text.alignment = TextAnchor.MiddleCenter;

            return btn;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Importata da Assets/Plugins/WebGLFilePicker.jslib
        [DllImport("__Internal")]
        private static extern void WebGLOpenFilePicker(string gameObjectName, string callbackMethod, string accept);
#endif

        public void OpenFilePicker()
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("Seleziona Avatar (.glb)", "", "glb");
            if (!string.IsNullOrEmpty(path))
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                _ = LoadAndApply(bytes);
            }
#elif UNITY_WEBGL
            // Il JS apre il file picker del browser e richiama OnWebGLFileSelected
            // (via SendMessage) col percorso nel FS virtuale di Emscripten.
            WebGLOpenFilePicker(gameObject.name, nameof(OnWebGLFileSelected), ".glb");
#else
            Debug.LogWarning("[GLBAvatarLoader] File picker non supportato su questa piattaforma.");
#endif
        }

        // Chiamato da WebGLFilePicker.jslib via SendMessage. Deve essere pubblico
        // e accettare una sola stringa. Il GameObject deve avere il nome atteso.
        public void OnWebGLFileSelected(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return;

            byte[] bytes = System.IO.File.ReadAllBytes(virtualPath);

            // Pulisci il file temporaneo dal FS virtuale
            try { System.IO.File.Delete(virtualPath); } catch { /* best-effort */ }

            _ = LoadAndApply(bytes);
        }

        // ──────────────────────────────────────────────────────────────
        // Caricamento GLB
        // ──────────────────────────────────────────────────────────────

        private async Task LoadAndApply(byte[] bytes)
        {
            MetaDynUGSPlayerController player = FindLocalPlayer();
            if (player == null)
            {
                _pendingAvatarData = bytes;
                Debug.Log("[GLBAvatarLoader] Player not spawned yet. Custom avatar cached and will be applied on spawn.");
                if (_gameMenu != null && _gameMenu.StatusText != null)
                {
                    _gameMenu.StatusText.text = "Avatar Uploaded! Click Start to Join.";
                    _gameMenu.StatusText.color = Color.green;
                }
                return;
            }

            var gltf = new GltfImport();
            bool loaded = await gltf.Load(bytes);
            if (!loaded)
            {
                Debug.LogError("[GLBAvatarLoader] File GLB non valido o corrotto.");
                return;
            }

            var container = new GameObject("GLB_AvatarImport");
            bool instantiated = await gltf.InstantiateMainSceneAsync(container.transform);
            if (!instantiated || container.transform.childCount == 0)
            {
                Debug.LogError("[GLBAvatarLoader] Scena GLB vuota — file non valido.");
                Destroy(container);
                return;
            }

            // Stacca il modello dal container temporaneo prima di distruggerlo
            GameObject model = container.transform.GetChild(0).gameObject;
            model.transform.SetParent(null, false);
            Destroy(container);

            LogMaterialSnapshot("after glTFast instantiate", model);
            ApplyResourceGltfMaterials(model, "after glTFast instantiate");
            LogMaterialSnapshot("after Resources material conversion", model);

            ApplyToPlayer(player, model);
        }

        // ──────────────────────────────────────────────────────────────
        // Applicazione avatar al player
        // ──────────────────────────────────────────────────────────────

        private void ApplyToPlayer(MetaDynUGSPlayerController player, GameObject model)
        {
            // Distruggi il modello GLB precedente PRIMA dello snapshot e del binding:
            // due scheletri con nomi di ossa identici rendono ambiguo il binding e
            // bloccano l'avatar in T-pose. DestroyImmediate perché Destroy è differito
            // a fine frame e sopravviverebbe durante BuildHumanAvatar/Rebind.
            if (_currentAvatarModel != null)
                DestroyImmediate(_currentAvatarModel);

            // AvatarSdkPlayerLipSync vive sul Player (non sull'avatar) e cachea riferimenti
            // a headMesh/teethMesh (SkinnedMeshRenderer dell'avatar AvatarSDK originale) nelle
            // sue coroutine di blink/smile/brow-raise (loop infinito). Se non lo fermiamo PRIMA
            // di distruggere l'avatar originale qui sotto, quelle coroutine sopravvivono e
            // accedono a headMesh.sharedMesh dopo la distruzione → MissingReferenceException.
            // Un avatar GLB custom non ha comunque le blend shape AvatarSDK (eyeBlinkLeft,
            // jawOpen, viseme...), quindi il lip sync va disabilitato in ogni caso.
            var lipSync = player.GetComponentInChildren<AvatarSdkPlayerLipSync>(true);
            if (lipSync != null)
            {
                lipSync.StopAllCoroutines();
                lipSync.enabled = false;
            }

            // Rimuovi del tutto l'avatar originale PRIMA di aggiungere il nuovo modello,
            // invece di limitarti a disabilitarne i Renderer: Player.UpdateFirstPersonVisibility
            // tiene una propria cache dei renderer (presa una sola volta in Awake) e li
            // riaccende uscendo dalla prima persona, facendo ricomparire l'avatar originale in
            // T-pose (il suo scheletro non è più animato dopo il rebind dell'Animator sul nuovo
            // GLB). DestroyImmediate per coerenza con _currentAvatarModel sopra — Destroy
            // sarebbe differito a fine frame.
            foreach (Transform root in FindOriginalAvatarRoots(player.transform))
                DestroyImmediate(root.gameObject);

            // Parenta senza alterare la transform locale (worldPositionStays=false): la
            // sistemazione definitiva di posizione/rotazione/scala/livello-suolo avviene
            // più sotto, dopo aver costruito l'Avatar humanoid (vedi commenti relativi —
            // root e pivot NON sono garantiti a identità/ai piedi per ogni rig, es. Mixamo).
            model.transform.SetParent(player.transform, false);

            UnityEngine.Avatar avatar = BuildHumanoidAvatar(model, out Dictionary<string, Transform> humanBoneTransforms);
            if (avatar == null || !avatar.isValid)
            {
                Debug.LogError("[GLBAvatarLoader] Avatar non valido — " +
                               "verifica che le ossa seguano lo standard RPM (LeftUpperArm, Hips, ecc.)");
                Destroy(model);
                return;
            }

            // Centra e orienta l'avatar sull'origine del player. La SCALA importata da
            // GLTFast va invece preservata: per alcuni rig (es. Mixamo) il root porta una
            // scala di compensazione reale (qui 0.01) perché la mesh è autorata 100× più
            // grande del dovuto — forzarla a 1 produce un avatar gigante. La rotazione del
            // root, invece, è spesso un artefatto della pipeline di export (correzione
            // d'assi pensata per il software sorgente, es. Blender Z-up→Y-up): GLTFast
            // converte già correttamente glTF→Unity per conto suo, quindi riapplicarla
            // qui produce una doppia rotazione (avatar disteso a terra). Per questo si
            // azzera SOLO la rotazione, non la scala. RPM esporta con root a identità su
            // entrambe, quindi per quei modelli questo passaggio resta un no-op.
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Aggiorna l'Animator del player con il nuovo Avatar
            player.Animator.avatar = avatar;
            player.Animator.Rebind();
            player.Animator.Update(0f);

            // Update the player controller's model root and refresh renderers
            player.ModelRoot = model.transform;
            player.RefreshModelRenderers();
            ApplyResourceGltfMaterials(model, "after animator rebind");
            LogMaterialSnapshot("after animator rebind conversion", model);

            // Allinea i piedi del modello al livello del terreno del player. Necessario
            // perché il pivot/root del GLB non è garantito trovarsi ai piedi del personaggio
            // (a differenza di RPM, che esporta sempre con i piedi all'origine — per quei
            // modelli questa correzione risulta ~0). Usiamo le ossa dei piedi/punte piedi,
            // non i bounds delle mesh: un bounds includerebbe anche eventuali accessori
            // (es. la spada "Maria_sword" di questo modello, o mantelli/code di capelli)
            // che potrebbero estendersi sotto i piedi e falsare il calcolo.
            if (TryGetLowestFootY(humanBoneTransforms, out float feetY))
            {
                float groundY = player.transform.position.y;
                model.transform.position += new Vector3(0f, groundY - feetY, 0f);
            }

            // Tieni traccia del modello applicato per poterlo rimuovere al prossimo upload
            _currentAvatarModel = model;
            StartCoroutine(VerifyMaterialsAfterFrames(model));

            Debug.Log("[GLBAvatarLoader] Avatar GLB applicato con successo!");
        }

        private IEnumerator VerifyMaterialsAfterFrames(GameObject model)
        {
            yield return null;
            ApplyResourceGltfMaterials(model, "next frame");
            LogMaterialSnapshot("next frame", model);

            yield return new WaitForSeconds(0.25f);
            ApplyResourceGltfMaterials(model, "0.25s later");
            LogMaterialSnapshot("0.25s later", model);

            yield return new WaitForSeconds(1f);
            ApplyResourceGltfMaterials(model, "1.25s later");
            LogMaterialSnapshot("1.25s later", model);
        }

        private static void ApplyResourceGltfMaterials(GameObject model, string stage)
        {
            Material metallicTemplate = Resources.Load<Material>(GltfMetallicMaterialPath);
            Material specularTemplate = Resources.Load<Material>(GltfSpecularMaterialPath);
            Material unlitTemplate = Resources.Load<Material>(GltfUnlitMaterialPath);

            if (metallicTemplate == null || specularTemplate == null || unlitTemplate == null)
            {
                Debug.LogError("[GLBAvatarLoader] Missing Resources/GLTFastShaders materials. " +
                               $"metallic={metallicTemplate != null}, specular={specularTemplate != null}, unlit={unlitTemplate != null}");
                return;
            }

            int changed = 0;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool rendererChanged = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || source.name.Contains("_MetaDynGLTFast"))
                        continue;

                    Material template = SelectGltfTemplate(source, metallicTemplate, specularTemplate, unlitTemplate);
                    Material converted = CloneWithTemplateShader(source, template);
                    if (converted == null)
                        continue;

                    materials[i] = converted;
                    rendererChanged = true;
                    changed++;
                }

                if (rendererChanged)
                    renderer.sharedMaterials = materials;
            }

            Debug.Log($"[GLBAvatarLoader] GLB material Resources conversion at {stage}: changed={changed}");
        }

        private static Material SelectGltfTemplate(Material source, Material metallic, Material specular, Material unlit)
        {
            string shaderName = source.shader != null ? source.shader.name.ToLowerInvariant() : string.Empty;
            string materialName = source.name.ToLowerInvariant();

            if (shaderName.Contains("unlit") || materialName.Contains("unlit"))
                return unlit;

            if (shaderName.Contains("specular") || materialName.Contains("specular"))
                return specular;

            return metallic;
        }

        private static Material CloneWithTemplateShader(Material source, Material template)
        {
            if (template == null || template.shader == null)
                return null;

            string[] keywords = source.shaderKeywords;
            int renderQueue = source.renderQueue;
            var converted = new Material(template)
            {
                name = $"{source.name}_MetaDynGLTFast",
                renderQueue = renderQueue
            };

            converted.CopyPropertiesFromMaterial(source);
            converted.shader = template.shader;
            converted.shaderKeywords = keywords;
            converted.renderQueue = renderQueue;
            return converted;
        }

        private static void LogMaterialSnapshot(string stage, GameObject model)
        {
            int logged = 0;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    string shaderName = material != null && material.shader != null ? material.shader.name : "<null>";
                    bool supported = material != null && material.shader != null && material.shader.isSupported;
                    string texture = DescribeTexture(material);

                    Debug.Log($"[GLBAvatarLoader] Material {stage}: renderer={renderer.name}, slot={i}, " +
                              $"mat={(material != null ? material.name : "<null>")}, shader={shaderName}, supported={supported}, tex={texture}");

                    logged++;
                    if (logged >= MaterialLogLimit)
                    {
                        Debug.Log($"[GLBAvatarLoader] Material {stage}: log limit reached ({MaterialLogLimit}).");
                        return;
                    }
                }
            }
        }

        private static string DescribeTexture(Material material)
        {
            if (material == null)
                return "<null>";

            foreach (string propertyName in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(propertyName);
                if (texture != null)
                    return $"{propertyName}:{texture.name}";
            }

            return "<none>";
        }

        // Rimuove il namespace Maya (es. "mixamorig:Hips" → "Hips", "mixamorig1:Spine" →
        // "Spine"): i rig Mixamo esportano le ossa con questo prefisso pur seguendo la
        // stessa naming convention RPM. ':' è il separatore di namespace standard Maya/
        // Mixamo e non compare in nomi di ossa legittimi (a differenza di '_', presente
        // anche in nomi come "HeadTop_End"), quindi è sicuro tagliare sull'ultimo.
        private static string StripNamespace(string boneName)
        {
            int idx = boneName.LastIndexOf(':');
            return idx >= 0 ? boneName[(idx + 1)..] : boneName;
        }

        // ──────────────────────────────────────────────────────────────
        // Costruzione Avatar humanoid da scheletro con nomi RPM
        // ──────────────────────────────────────────────────────────────

        private UnityEngine.Avatar BuildHumanoidAvatar(GameObject modelRoot, out Dictionary<string, Transform> humanBoneTransforms)
        {
            Transform[] allTransforms = modelRoot.GetComponentsInChildren<Transform>(true);

            // Chiave = nome osso senza namespace (es. "mixamorig:Hips" → "Hips"), così il
            // matching degli alias funziona sia su rig RPM che Mixamo. Il valore mantiene il
            // Transform con il nome ORIGINALE, che è quello richiesto come boneName/SkeletonBone.
            var boneDict = new Dictionary<string, Transform>();
            foreach (Transform t in allTransforms)
            {
                string normalized = StripNamespace(t.name);
                if (!boneDict.ContainsKey(normalized))
                    boneDict[normalized] = t;
            }

            // Costruisci HumanBone[] provando ogni alias per ogni osso humanoid
            var humanBones  = new List<HumanBone>();
            var missingRequired = new List<string>();
            humanBoneTransforms = new Dictionary<string, Transform>();

            foreach (var (humanName, aliases, required) in BoneMap)
            {
                Transform foundTransform = null;
                foreach (string alias in aliases)
                    if (boneDict.TryGetValue(alias, out foundTransform)) break;

                if (foundTransform != null)
                {
                    humanBones.Add(new HumanBone
                    {
                        humanName = humanName,
                        boneName  = foundTransform.name,
                        limit     = new HumanLimit { useDefaultValues = true },
                    });
                    humanBoneTransforms[humanName] = foundTransform;
                }
                else if (required)
                {
                    missingRequired.Add($"{humanName} (cercato: {string.Join("/", aliases)})");
                }
            }

            if (missingRequired.Count > 0)
            {
                Debug.LogError($"[GLBAvatarLoader] Ossa obbligatorie non trovate:\n" +
                               string.Join("\n", missingRequired));
                return null;
            }

            // Forza la T-pose sulle braccia PRIMA di catturare lo scheletro.
            // Gli avatar RPM/Mixamo sono esportati in A-pose (braccia ~57° verso il
            // basso). BuildHumanAvatar usa la posa fornita come riferimento "muscle
            // zero" e si aspetta una T-pose (braccia orizzontali): senza questa
            // correzione le animazioni standard arrivano alle braccia con un offset
            // di ~57°. Gambe/spina/testa sono identiche in A-pose e T-pose, per questo
            // erano già corrette.
            EnforceArmTPose(modelRoot, humanBoneTransforms);

            // Costruisci SkeletonBone[] da tutti i transform (nomi unici)
            var skeletonBones = new List<SkeletonBone>();
            var seenNames     = new HashSet<string>();
            foreach (Transform t in allTransforms)
            {
                if (!seenNames.Add(t.name)) continue;
                skeletonBones.Add(new SkeletonBone
                {
                    name     = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale    = t.localScale,
                });
            }

            var description = new HumanDescription
            {
                human             = humanBones.ToArray(),
                skeleton          = skeletonBones.ToArray(),
                upperArmTwist     = 0.5f,
                lowerArmTwist     = 0.5f,
                upperLegTwist     = 0.5f,
                lowerLegTwist     = 0.5f,
                armStretch        = 0.05f,
                legStretch        = 0.05f,
                feetSpacing       = 0f,
                hasTranslationDoF = false,
            };

            return AvatarBuilder.BuildHumanAvatar(modelRoot, description);
        }

        // Ruota le braccia in T-pose (orizzontali, dritte) usando gli assi del modello.
        // Necessario perché gli avatar RPM/Mixamo sono in A-pose; vedi nota nel chiamante.
        private static void EnforceArmTPose(GameObject modelRoot, Dictionary<string, Transform> bones)
        {
            // Direzione "fuori" relativa all'orientamento del modello (robusto anche se
            // il player è ruotato): braccio destro lungo +X del modello, sinistro lungo -X.
            Vector3 rightDir = modelRoot.transform.right;
            AlignArm(bones, "LeftUpperArm",  "LeftLowerArm",  "LeftHand",  -rightDir);
            AlignArm(bones, "RightUpperArm", "RightLowerArm", "RightHand",  rightDir);
        }

        // Allinea il segmento braccio (upper) e avambraccio (lower) lungo worldDir,
        // così l'intero arto risulta dritto e orizzontale.
        private static void AlignArm(
            Dictionary<string, Transform> bones,
            string upperName, string lowerName, string handName, Vector3 worldDir)
        {
            bones.TryGetValue(upperName, out Transform upper);
            bones.TryGetValue(lowerName, out Transform lower);
            bones.TryGetValue(handName,  out Transform hand);
            if (upper == null) return;

            // Target per allineare il braccio superiore: il gomito se c'è, altrimenti la mano.
            Transform upperTarget = lower != null ? lower : hand;
            if (upperTarget != null)
            {
                Vector3 cur = (upperTarget.position - upper.position).normalized;
                if (cur.sqrMagnitude > 0f)
                    upper.rotation = Quaternion.FromToRotation(cur, worldDir) * upper.rotation;
            }

            // Raddrizza il gomito: allinea l'avambraccio (gomito→mano) lungo worldDir.
            if (lower != null && hand != null)
            {
                Vector3 cur = (hand.position - lower.position).normalized;
                if (cur.sqrMagnitude > 0f)
                    lower.rotation = Quaternion.FromToRotation(cur, worldDir) * lower.rotation;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Utility
        // ──────────────────────────────────────────────────────────────

        // Trova la Y mondiale più bassa tra i piedi (preferendo le punte piedi, più vicine
        // al punto di contatto col terreno, quando presenti). Usato per lo snap a terra:
        // a differenza dei bounds delle mesh, le ossa non risentono di accessori/props
        // che si estendono sotto i piedi.
        private static bool TryGetLowestFootY(Dictionary<string, Transform> bones, out float feetY)
        {
            feetY = float.PositiveInfinity;
            bool found = false;

            foreach (var (toesName, footName) in new[] { ("LeftToes", "LeftFoot"), ("RightToes", "RightFoot") })
            {
                if (!bones.TryGetValue(toesName, out Transform foot))
                    bones.TryGetValue(footName, out foot);

                if (foot != null)
                {
                    feetY = Mathf.Min(feetY, foot.position.y);
                    found = true;
                }
            }

            return found;
        }

        private static MetaDynUGSPlayerController FindLocalPlayer()
        {
            foreach (MetaDynUGSPlayerController p in FindObjectsByType<MetaDynUGSPlayerController>(FindObjectsSortMode.None))
                if (p.IsOwner)
                    return p;
            return null;
        }

        // Trova i GameObject dell'avatar originale da rimuovere, in modo indipendente dai
        // nomi/dalla struttura del prefab (es. "model" per i prefab AvatarSDK, "Geometry"+
        // "Skeleton" per Player.prefab, ecc.): per ogni Renderer (e per il rootBone degli
        // SkinnedMeshRenderer) risale la gerarchia fino al figlio diretto del player, che
        // rappresenta la radice dell'avatar da eliminare.
        private static HashSet<Transform> FindOriginalAvatarRoots(Transform playerRoot)
        {
            var roots = new HashSet<Transform>();

            Transform DirectChildOf(Transform t)
            {
                while (t != null && t.parent != playerRoot)
                    t = t.parent;
                return t;
            }

            foreach (Renderer r in playerRoot.GetComponentsInChildren<Renderer>(true))
            {
                Transform root = DirectChildOf(r.transform);
                if (root != null) roots.Add(root);

                if (r is SkinnedMeshRenderer smr && smr.rootBone != null)
                {
                    Transform boneRoot = DirectChildOf(smr.rootBone);
                    if (boneRoot != null) roots.Add(boneRoot);
                }
            }

            return roots;
        }
    }
}
