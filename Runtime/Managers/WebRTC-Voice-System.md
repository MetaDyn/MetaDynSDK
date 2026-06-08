# WebRTC Voice System

## Overview
WebRTC-based voice chat system for Unity 6 WebGL builds, replacing Photon Voice due to incompatible libopus.bc native libraries.

## Architecture

### Components
1. **WebRTCManager.cs** - Unity NetworkBehaviour managing voice connections
   - Location: `Assets/MetaDyn/Managers/WebRTCManager.cs`
   - **Attached to: Player prefab** (NOT Runner, NOT scene objects)
   - Each spawned player gets their own instance
   - Only local player's instance (`HasInputAuthority`) registers callbacks and initializes microphone
   - Handles Fusion callbacks and WebRTC lifecycle

2. **WebRTCVoice.jslib** - JavaScript WebRTC implementation
   - Location: `Assets/Plugins/WebGL/WebRTCVoice.jslib`
   - Uses browser's native RTCPeerConnection API
   - Handles microphone capture and audio playback
   - Queues signals that arrive before microphone initializes

### Communication Flow
```
Player 1 Microphone → WebRTC_Init() → Browser MediaDevices API
                    ↓
Player 1 joins room → OnPlayerJoined() → WebRTC_Connect(playerId, shouldInitiate)
                    ↓
Initiator creates Offer → OnReceiveDescriptionFromJS() → Fusion ReliableData
                    ↓
Player 2 receives Offer → OnReliableDataReceived() → WebRTC_HandleSignal()
                    ↓
Player 2 creates Answer → Send back via ReliableData
                    ↓
ICE candidates exchanged → Peer connection established → Audio streams
```

## Key Implementation Details

### Player Identification
- Uses Fusion `PlayerId` for peer identification
- Lower PlayerId initiates connection to avoid duplicate offers
- Local player renames GameObject to `WebRTCManager_{PlayerId}` (e.g., "WebRTCManager_1")
- JavaScript stores local PlayerId to construct correct GameObject name for SendMessage

### Network Transport
- **Signaling**: Fusion ReliableData (supports 1-2KB SDP descriptions, bypasses 512-byte RPC limit)
- **Sender ID Embedded**: Signal data wrapped with sender PlayerId to ensure correct attribution
- **Audio**: Direct P2P WebRTC (not through Fusion/Photon servers)
- **ICE Candidates**: Also via ReliableData

### Per-Player Instance
WebRTCManager is attached to **Player prefab**, not Runner:
- Each player spawns with their own WebRTCManager instance
- Only local player (`HasInputAuthority`) initializes microphone and registers callbacks
- Each instance manages connections to all other players
- Remote player clones do nothing (no authority)

### Async Microphone Handling
- Microphone initialization is async (awaits browser permission)
- Signals arriving before mic is ready are queued in `pendingSignals[]`
- Once mic initializes, all queued signals are processed
- Prevents race conditions during player spawning

## Files Modified/Created

### New Files Created
- `Assets/Plugins/WebGL/WebRTCVoice.jslib` - JavaScript WebRTC wrapper
  - Manages browser WebRTC API
  - Handles microphone capture via `getUserMedia()`
  - Creates RTCPeerConnection instances per remote player
  - Queues early signals with `pendingSignals[]`
  - Explicitly calls `.play()` on audio elements

- `Assets/MetaDyn/Managers/WebRTCManager.cs` - Unity voice manager
  - NetworkBehaviour with INetworkRunnerCallbacks
  - Wraps signals with sender ID using SignalWrapper class
  - Only registers callbacks if `HasInputAuthority`
  - Renames GameObject to unique name per player
  - Uses ReliableData instead of RPCs

### Packages Deleted
- `Assets/Photon/PhotonVoice/` (entire package) - Incompatible libopus.bc with Unity 6

### Packages Upgraded
- Photon Fusion: 2.0.4 → 2.0.9 (Unity 6 compatibility)

### Prefabs Cleaned
- `Assets/Common/Runner.prefab` - Removed missing PhotonVoiceClient component references
- `Assets/Common/Player.prefab` - Removed missing PhotonVoiceRecorder component references, **Added WebRTCManager component**

### Cache Cleaned
- `Library/Bee/` - Deleted to clear old opus build artifacts

## Known Limitations

### Mesh Topology
**Current**: Full mesh P2P (each player connects to every other player)
**Issue**: Does NOT scale beyond ~6 players due to bandwidth
- Each player uploads audio N-1 times
- Each player downloads and decodes N-1 streams

### Future Migration Required
**Recommended**: LiveKit SFU integration
- User already has LiveKit account (used with Hyperfy)
- SFU (Selective Forwarding Unit) scales to 50+ concurrent users
- Would require replacing jslib with LiveKit JavaScript SDK

## Dependencies

### Unity Packages
- Photon Fusion 2.0.9 (multiplayer networking)
- PhotonLibs (WebSocket transport - DO NOT DELETE)

### Browser Requirements
- WebRTC support (all modern browsers)
- HTTPS required for microphone permissions (except localhost)

## Setup Instructions

### 1. Add WebRTCManager to Player Prefab
- Open `Assets/Common/Player.prefab`
- Add `WebRTCManager` component
- Ensure Player prefab has `NetworkObject` component
- Save prefab

### 2. Unity Dependencies
- Ensure using Unity 6000.0.62f1 or later
- Photon Fusion 2.0.9 installed
- PhotonLibs (WebSocket) present (DO NOT delete)
- System.Linq namespace available

### 3. Build Settings
- Platform: WebGL
- Compression: Brotli (recommended)
- Deploy to HTTPS server (required for microphone permissions)

## Testing Checklist

1. ✅ Build for WebGL
2. ✅ Deploy to HTTPS server (localhost works for testing)
3. ✅ Open Player 1 in Chrome
4. ✅ Accept microphone permission prompt
5. ✅ Open Player 2 in different browser or tab (Brave/Firefox)
6. ✅ Both players should spawn successfully
7. ✅ Check browser console for WebRTC logs:
   - "WebRTC: Microphone acquired for player X"
   - "[WebRTC] Connecting to player X, initiating: True/False"
   - "[WebRTC] Received signal from player X"
   - "WebRTC: Playing remote audio from X"
   - "[WebRTC] Peer X started speaking (level: 0.xxxx)"
   - "[WebRTC] Peer X stopped speaking"
8. ✅ **TEST VOICE**: Speak into microphone - other player should hear you clearly
9. ✅ Audio quality: Crystal clear, no distortion
10. ✅ No echo when using separate computers or headphones
11. ✅ **TEST SPATIAL AUDIO**: Move players apart - volume should decrease with distance
12. ✅ **TEST LIP SYNC**: Speak and verify remote player's mouth moves in sync with voice

### Expected Behavior
- **Same computer testing**: You may hear yourself due to mic picking up speaker output (normal)
- **Separate computers/headphones**: No self-hearing, clean audio only
- **Footsteps heard twice**: Microphone captures game audio (can be filtered later)

## Troubleshooting

### "webRTCContext is not defined"
- Missing `__deps` declarations in jslib functions
- Fixed: All functions declare `__deps: ['$webRTCContext']`

### "Payload is too large" RPC error
- WebRTC SDP descriptions exceed 512-byte RPC limit
- Fixed: Use `SendReliableDataToPlayer()` instead of RPCs

### Players don't spawn / Fusion disconnects
- Missing script references on prefabs (from deleted PhotonVoice)
- Check Unity Console for "Script (Missing)" errors
- Clean up prefabs and rebuild

### No audio between players
- Check browser console for peer connection errors
- Verify both players accepted microphone permissions
- Check firewall/NAT (STUN server helps: `stun:stun.l.google.com:19302`)

## Code References

### JavaScript to Unity Communication
Uses unique GameObject names per player:
```javascript
const gameObjectName = 'WebRTCManager_' + webRTCContext.localPlayerId;
SendMessage(gameObjectName, 'OnReceiveDescriptionFromJS', jsonData);
SendMessage(gameObjectName, 'OnReceiveIceCandidateFromJS', jsonData);
```

### Unity to JavaScript Communication
```csharp
WebRTC_Init(playerId);  // Request microphone, store player ID
WebRTC_Connect(peerId, isInitiator);  // Create peer connection
WebRTC_HandleSignal(peerId, jsonSignal);  // Handle SDP/ICE
WebRTC_DisconnectPeer(peerId);  // Close connection
```

### Fusion Signaling with Sender Wrapping
```csharp
// Wrap signal with sender ID
var wrapper = new SignalWrapper { senderId = Runner.LocalPlayer.PlayerId, signal = jsonSignal };
string wrappedSignal = JsonUtility.ToJson(wrapper);
byte[] data = System.Text.Encoding.UTF8.GetBytes(wrappedSignal);
Runner.SendReliableDataToPlayer(targetPlayer, ReliableKey.FromInts(0, 1), data);
```

### Callback Registration (Local Player Only)
```csharp
if (Object.HasInputAuthority && Runner != null)
{
    Runner.AddCallbacks(this);  // Only local player registers
}
```

### Async Signal Queueing (JavaScript)
```javascript
if (!this.localStream) {
    console.log("WebRTC: Queuing signal (mic not ready)");
    this.pendingSignals.push({ peerId: peerIdStr, signal: signalData });
    return;
}
// Process queued signals after mic initializes
```

## Migration History

### Why Replace Photon Voice?
- Photon Voice uses native libopus.bc for audio encoding
- Unity 6 upgraded Emscripten compiler (incompatible with old opus builds)
- Spent hours attempting to fix divide-by-zero errors
- Decision: Use browser's native WebRTC instead (no opus needed)

### Version Information
- Unity 6000.0.62f1
- Fusion 2.0.9
- Browser WebRTC (native, no external library versions)

## Implementation Status

### ✅ COMPLETE - Fully Functional (2025-12-12, Updated 2025-12-20)
- WebRTC voice chat working perfectly
- Crystal clear audio quality
- No echo or feedback issues (with headphones/separate computers)
- Proper signaling via Fusion ReliableData
- Async microphone initialization handled correctly
- Unique per-player GameObject naming
- All race conditions resolved
- **✅ Spatial Audio** - VERIFIED WORKING (2025-12-20)
- **✅ Lip Sync** - VERIFIED WORKING (2025-12-20)

### Test Results
- **2 players**: ✅ Perfect
- **Audio Quality**: ✅ Crystal clear
- **Latency**: ✅ Minimal (P2P connection)
- **Browser Support**: ✅ Chrome, Brave, Firefox tested
- **Microphone Permissions**: ✅ Working on HTTPS
- **Same Computer Testing**: ✅ Works (expected mic feedback from speakers)
- **Spatial Audio**: ✅ 3D positioning working with distance-based falloff
- **Lip Sync**: ✅ Mouth animations sync with WebRTC audio levels

### Lip Sync Integration (2025-12-20)
- Uses `AvatarSdkPlayerLipSync.cs` for procedural viseme animation
- WebRTCManager detects audio levels via JavaScript ring buffer
- Calls `StartSpeaking()`/`StopSpeaking()` when threshold crossed
- Fixed conflict: Added `webRTCControlled` flag to prevent AudioSource Update() from cancelling WebRTC triggers
- Audio level detection threshold: 0.01f RMS
- Supports both WebRTC triggering AND AudioSource triggering (no conflicts)

## Next Steps (Future Work)

1. **Scale Testing** - Verify performance with 3-6 players
2. **LiveKit Migration** - Replace mesh with SFU for production scale (50+ players)
3. ~~**Spatial Audio** - Add 3D positioning based on player distance~~ ✅ COMPLETE
4. **Mute Controls** - UI for muting individual players or self
5. ~~**Audio Indicators** - Visual feedback showing who's speaking~~ ✅ COMPLETE (lip sync)
6. **Voice Activation** - Filter out non-voice sounds (footsteps, ambient)
7. **Error Recovery** - Reconnect logic for failed peer connections
8. **Audio Settings** - Volume controls, mic sensitivity adjustment
