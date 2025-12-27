# Unity File Tracking Issue Summary

**Problem**: Unity compilation system is tracking files that have been deleted during test runs, causing CS2001 errors like 'Source file could not be found'.

**Symptoms**:
- Tests create temporary files during execution
- Files are deleted by cleanup fixtures
- Unity compiler still references deleted files in subsequent compilations
- Shows errors like: `error CS2001: Source file 'D:\code\Yamu\Assets/ErrorScript1.cs' could not be found`

**Root Cause**:
Unity's compilation system caches file references and doesn't immediately update when files are deleted externally. AssetDatabase.Refresh() should detect deletions but may not be sufficient.

**Current Status**:
✅ New file creation works (refresh_assets fixes this)
❌ File deletion cleanup not properly detected by Unity
❌ Multiple test runs leave orphaned file references

**Potential Solutions Based on Research**:

1. **Use AssetDatabase.DeleteAsset()** instead of direct file deletion - Unity's proper way to delete assets
2. **Force AssetDatabase.Refresh()** with ImportAssetOptions.ForceUpdate after deletions
3. **AssetDatabase.ImportAsset()** on parent directory after deletions
4. **Restart Unity Editor** - Most reliable solution (forces complete AssetDatabase refresh)
5. **Use EditorApplication.delayCall** to defer operations until next editor frame

**From Unity Documentation/Forums**:
- CS2001 errors are common when files are deleted externally or through version control
- Unity's AssetDatabase can become out of sync with file system
- Deleting scripts in quick succession causes CS2001 compiler errors (known Unity issue)
- Most reliable fix is restarting Unity Editor
- Alternative: Use Unity's asset management APIs instead of direct file operations

**For Test Environment**:
- Consider using AssetDatabase.DeleteAsset() instead of os.remove()
- Add AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate) after deletions
- May need to restart Unity between test sessions to fully clear cached references