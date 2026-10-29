// one per system that has state worth keeping. its section goes in the save file and, if the
// session asks for it, to a joining friend. Version is bumped when Write changes, Read gets the
// version the section was written with and migrates
public interface IStateful
{
	string Key { get; }

	int Version { get; }

	void Write(StateWriter writer);

	void Read(StateReader reader, int version);
}
