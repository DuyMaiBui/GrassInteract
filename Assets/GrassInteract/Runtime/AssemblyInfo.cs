// Exposes internal runtime types/members to the editor + EditMode test assemblies.
// (InstanceColliderPool for tests; ScatterField edit-mode drivers for the editor companion.)
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GrassInteract.Editor")]
[assembly: InternalsVisibleTo("GrassInteract.EditorTests")]
