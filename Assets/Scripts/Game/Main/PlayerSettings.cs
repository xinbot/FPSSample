public class PlayerSettings 
{
    public string PlayerName;
    public int CharacterType;
    public short TeamId;

    public void Serialize(ref NetworkWriter writer)
    {
        writer.WriteString("playerName", PlayerName);
        writer.WriteInt16("characterType", (short)CharacterType);
        writer.WriteInt16("teamId", TeamId);
    }

    public void Deserialize(ref NetworkReader reader)
    {
        PlayerName = reader.ReadString();
        CharacterType = reader.ReadInt16();
        TeamId = reader.ReadInt16();
    }
}
