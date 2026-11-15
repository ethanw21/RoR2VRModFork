// Reference-only stand-in for the game's Decalicious.dll (Three Eyed Games' deferred decal
// renderer, which Risk of Rain 2 ships). The game's assemblies cannot be put in this repository
// or downloaded by a build server, and this one is not in the RiskOfRain2.GameLibs package, so
// the handful of members the mod names are declared here. Nothing from this file is shipped:
// at runtime the mod binds to the game's real Decalicious.dll.
//
// Only what RoR2VRMod actually references is declared. Everything else it does with the decal
// renderer goes through reflection or IL matching by name (see CameraFixes.OnPreRenderIL).
namespace ThreeEyedGames
{
    public class DecaliciousRenderer : UnityEngine.MonoBehaviour
    {
        // 'protected' in the real assembly; the mod compiles against a publicized copy.
        public UnityEngine.Camera _camera;
    }
}
