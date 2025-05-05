using UnityEditor;
using UnityEngine;

public class ModelImporterProcessor : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        ModelImporter importer = (ModelImporter)assetImporter;

        // Automatically enable read/write for imported meshes
        importer.isReadable = true;
    }
}