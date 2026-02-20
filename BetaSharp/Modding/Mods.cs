using System.Collections.Immutable;
using System.Reflection;

namespace BetaSharp.Modding;

public class Mods
{
    public static ImmutableList<IMod> ModRegistry { get; private set; } = null!;

    public static void LoadMods(string modsFolder, Side side)
    {
        if (!Directory.Exists(modsFolder))
        {
            Directory.CreateDirectory(modsFolder);
            return; // It's empty.
        }
        string[] modsFiles = Directory.GetFiles(modsFolder).Where(f => f.EndsWith(".dll")).ToArray();

        Log.Info($"Found {modsFiles.Length} mods in mods folder.");
        if (modsFiles.Length == 0)
        {
            return;
        }

        List<IMod> loadedMods = [];

        Log.Info($"Loading {modsFiles.Length} mods...");
        foreach (string file in modsFiles)
        {
            Log.Info("Loading mod " + file + "...");

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(System.IO.Path.GetFullPath(file));
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load mod {file}: {ex}");
                continue;
            }

            Type? modType = null;
            try
            {
                Type[] types = assembly.GetTypes();
                foreach (Type t in types)
                {
                    if (t.GetInterface(nameof(IMod)) != null)
                    {
                        modType = t;
                        break;
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                Log.Error($"Failed to get types from mod {file}: {ex}");
                continue;
            }

            if (modType == null)
            {
                Log.Warn($"Mod {file} has no class that implements {nameof(IMod)}, and as such will not be initialized.");
                continue;
            }
            if (Attribute.GetCustomAttribute(modType, typeof(ModSideAttribute)) is ModSideAttribute modSideAttr)
            {
                if (modSideAttr.Side != side && modSideAttr.Side != Side.Both)
                {
                    Log.Info($"Skipping mod {file} because it is marked for side {modSideAttr.Side} and not {side}.");
                    continue;
                }
            }

            Log.Info($"Initializing mod {file}...");
            IMod modInstance;
            try
            {
                modInstance = (IMod)Activator.CreateInstance(modType)!;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to create mod instance for mod {file}: {ex}");
                continue;
            }

            loadedMods.Add(modInstance);
            modInstance.Initialize();
            Log.Info($"Successfully initialized mod {file}");
        }

        ModRegistry = loadedMods.ToImmutableList();

        Log.Info("Running PostInitialize on mods...");
        foreach (IMod mod in ModRegistry)
        {
            Log.Info($"Running PostInitialize of mod {mod.Name}...");
            mod.PostInitialize();
            Log.Info($"PostInitialize of mod {mod.Name} complete.");
        }
        Log.Info("Mods initialized.");
    }
}
