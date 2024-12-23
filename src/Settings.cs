using ProtoBuf;

namespace SpeedBoat;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Settings {
    public float RaftSpeedMultiplier = 2f;
    public float SailboatSpeedMultiplier = 4f;
}
