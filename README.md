# MetaDyn Master SDK

Official Unity Package Manager SDK for building MetaDyn spaces: browser-first immersive worlds with Unity Gaming Services, Netcode for GameObjects, Vivox communication, Supabase identity hooks, creator tooling, inventory bundling, and embodied AI systems.

Package name: `com.metadyn.sdk`
Current release: `v1.3.2`
Unity version: `6000.0` or newer
Primary target: WebGL

## What This SDK Provides

MetaDyn is a reusable Unity toolkit for creating deployable social 3D spaces. It is designed as the portable platform layer used by MetaDyn starter spaces and downstream client projects.

Core systems include:

- UGS/NGO multiplayer session flow with Lobby, Relay, deterministic space joins, and player spawning.
- Vivox voice and text integration for social communication.
- WebGL-first auth bridge support for dashboard SSO and Unity fallback login.
- Creator-facing runtime components such as seats, doors, light switches, triggers, interactables, projection surfaces, and entrance points.
- Mobile WebGL controls and platform detection helpers.
- Social Hub UI and Supabase-backed social data manager.
- Inventory metadata and product bundling tools for marketplace-ready digital assets.
- Embodied AI components for perception, vision, movement, memory, voice, and spatial behavior.
- Editor tooling for SDK dashboard, validation, deployment, Netlify/Vercel/GitHub deployment support, and SDK update checks.

## Installation

### Option 1: Unity Package Manager Git URL

In Unity:

1. Open **Window > Package Manager**.
2. Click **+**.
3. Choose **Add package from git URL...**.
4. Enter:

```text
https://github.com/MetaDyn/MetaDynSDK.git#v1.3.2
```

For the latest `main` branch instead of a pinned release:

```text
https://github.com/MetaDyn/MetaDynSDK.git
```

Pinned tags are recommended for production projects.

### Option 2: `Packages/manifest.json`

Add this dependency to your Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.metadyn.sdk": "https://github.com/MetaDyn/MetaDynSDK.git#v1.3.2"
  }
}
```

Unity will resolve the package and its declared dependencies.

## Dependencies

The package declares these Unity package dependencies:

```json
{
  "com.unity.netcode.gameobjects": "2.1.0",
  "com.unity.services.lobby": "1.2.1",
  "com.unity.services.relay": "1.1.0",
  "com.unity.services.vivox": "16.4.2",
  "com.unity.cloud.gltfast": "6.11.0"
}
```

Your project must also be linked to Unity Gaming Services if you intend to use multiplayer, Relay, Lobby, or Vivox.

## Repository Layout

```text
MetaDynSDK/
├── package.json              # UPM package identity and dependencies
├── Runtime/                  # Runtime systems, components, prefabs, assets
├── Editor/                   # Editor windows, inspectors, deployment tools
└── README.md                 # This file
```

Assembly definitions:

- `Runtime/MetaDyn.Runtime.asmdef`
- `Editor/MetaDyn.Editor.asmdef`

## Quick Start

1. Install the SDK using the Git URL above.
2. Link your Unity project to Unity Gaming Services.
3. Enable/configure Lobby, Relay, and Vivox services in your Unity Cloud project.
4. Open the MetaDyn dashboard from **Tools > MetaDyn > Dashboard**.
5. Run the SDK validation tools from the MetaDyn editor menu.
6. Add or configure required scene managers, including session, auth, UI, and player/avatar registry objects.
7. Add `EntrancePoint` components to valid spawn locations.
8. Configure a `MetaDynRuntimeConfig` for the target space ID, room name, owner ID, and environment.
9. Build for WebGL and deploy through your preferred hosting path.

## Important Runtime Concepts

### Space Identity

MetaDyn sessions are keyed by the configured space identity. The runtime config should use a stable Supabase UUID-style `spaceId` so users deterministically join the intended room.

### Scene Entry Points

Every playable space should include at least one `EntrancePoint`. The session/spawn flow uses these points to avoid spawning players at the world origin.

### Avatar Registry

`MetaDynUGSAvatarRegistry` defines available NGO-ready player prefabs and maps user avatar selection indices to network-spawnable prefabs.

### Runtime GLB Avatars On WebGL

Runtime-loaded GLB avatars use glTFast. WebGL builds must include glTFast shader-backed materials in Resources so Unity does not strip the shaders needed by materials created at runtime.

Required Resources materials:

```text
Assets/Resources/GLTFastShaders/gltfast_metallic.mat
Assets/Resources/GLTFastShaders/gltfast_specular.mat
Assets/Resources/GLTFastShaders/gltfast_unlit.mat
```

Create or refresh them from Unity:

```text
Tools > MetaDyn > Create GLTFast Shader Materials (WebGL Fix)
```

The runtime avatar loader should convert imported GLB materials onto clones of these Resources materials, preserving imported textures, colors, keywords, and render state. Do not convert runtime GLB avatars to generic URP Lit fallback materials; that can flatten avatars to white and lose glTF material properties. If a WebGL avatar starts correct and later turns magenta/pink, check that the Resources materials exist and that the browser console does not report missing or unsupported glTFast shaders.

### Authentication Modes

The SDK supports:

- Guest/no-auth mode.
- Dashboard web auth via browser bridge.
- Unity fallback login UI.

Projects can choose the mode that fits their deployment. WebGL production deployments usually use dashboard SSO.

### AI Keys And Secrets

The SDK does not ship with provider API keys. Configure AI, speech, memory, and backend credentials per project through secure project settings, environment-specific config, or private assets that are not committed to public repositories.

Do not commit live OpenAI, OpenRouter, ElevenLabs, Supabase service-role, GitHub, or deployment tokens into prefabs, scenes, scriptable objects, source code, or package docs.

## Major Systems

### Multiplayer

Key files:

- `Runtime/Networking/MetaDynUGSSessionService.cs`
- `Runtime/Networking/MetaDynUGSPlayerController.cs`
- `Runtime/Networking/MetaDynUGSAvatarRegistry.cs`
- `Runtime/Networking/MetaDynVivoxService.cs`

The active networking baseline is UGS with Netcode for GameObjects, Lobby, Relay, and Vivox.

### Creator Components

Reusable world-building components live under:

```text
Runtime/Core/Components/
```

Examples:

- `EntrancePoint`
- `SeatHotspot`
- `Interactable`
- `Trigger`
- `ProjectionSurface`
- `MetaDynDoor`
- `MetaDynLightSwitch`
- `MetaDynPlatformDetector`

### UI And Mobile WebGL

UI helpers and prefabs include:

- `Runtime/UI/`
- `Runtime/Core/UI/`
- `Runtime/Core/Starter/UIGameMenu.cs`
- `Runtime/UI/PointerInputUtility.cs`

The SDK includes mobile joystick/jump controls and platform-specific toggles for WebGL/mobile experiences.

### Inventory And Marketplace

Inventory tooling includes:

- `Runtime/Inventory/MetaDynItemMetadata.cs`
- `Editor/Inventory/MetaDynItemBundler.cs`
- `Editor/Inventory/MetaDynItemMetadataEditor.cs`

Use `MetaDynItemMetadata` on item prefabs, then package assets through the Product Bundler editor tool.

### Embodied AI

AI runtime systems include:

- `Runtime/AI/MetaDynVoiceController.cs`
- `Runtime/AI/AIPerceptionManager.cs`
- `Runtime/AI/AIEye.cs`
- `Runtime/AI/AIMovementController.cs`
- `Runtime/AI/AIMemoryManager.cs`
- `Runtime/AI/HeadLookController.cs`

These systems provide perception context, vision capture, speech orchestration, semantic memory hooks, movement, and gaze behavior for embodied agents.

## Editor Tools

MetaDyn editor tools live under:

```text
Editor/Core/MetaDynSDK/
```

Useful entry points:

- **Tools > MetaDyn > Dashboard**
- **Tools > MetaDyn > Project Configuration**
- **Tools > MetaDyn > SDK Sync Check**
- **Tools > MetaDyn > Product Bundler**

The dashboard can check the remote SDK manifest and surface available package updates.

## WebGL Deployment Notes

MetaDyn is WebGL-first. Production deployments should use:

- HTTPS hosting.
- Brotli or gzip compression configured with correct `Content-Encoding`.
- WebSocket-compatible hosting/proxy configuration for UGS Relay/Vivox flows.
- A CDN such as Cloudflare or equivalent edge caching for build files.

The SDK includes deployment tooling for server, Netlify, Vercel, and GitHub-oriented workflows, but each production environment should still verify headers, compression, routing, and auth-domain behavior.

GitHub Pages deployments use an atomic Git Data API pipeline: WebGL build files are uploaded as Git blobs, assembled into one tree and commit, then published by updating `gh-pages` after every file is ready. GitHub blocks repository files larger than 100 MiB, so larger WebGL builds should use Vercel, Netlify, or another static host that supports large Unity data files.

## Versioning And Releases

Release tags follow the form:

```text
v1.3.2
```

When releasing a new SDK version, update all version references together:

- `package.json`
- `Editor/Core/MetaDynSDK/MetaDynSDK.cs`
- `Editor/Core/MetaDynSDK/MetaDynSDKManifest.json`

Then create and push a matching Git tag. The SDK updater uses the tag archive URL:

```text
https://github.com/MetaDyn/MetaDynSDK/archive/refs/tags/v1.3.2.zip
```

## Security Guidance

This is a public SDK repository. Treat it as distributable package code.

Never commit:

- Provider API keys.
- Supabase service-role keys.
- GitHub tokens.
- SSH keys.
- Deployment credentials.
- Customer or environment secrets.

Keep sensitive values in private project-level configuration, environment variables, secure deployment settings, or private Unity assets excluded from public distribution.

## Support

MetaDyn website:

```text
https://metadyn.xyz
```

Official SDK repository:

```text
https://github.com/MetaDyn/MetaDynSDK
```

MetaDyn Unity SDK is proprietary software.

This SDK is provided only to authorized MetaDyn Creators, Brands, Customers, Partners, Sponsors, Supporters, and Developers. Use requires an active MetaDyn account, subscription, or written agreement.

Redistribution, resale, sublicensing, public posting, or use outside the MetaDyn platform is strictly prohibited.

Read More:
```text
https://github.com/MetaDyn/MetaDynSDK/LICENSE.md
```
