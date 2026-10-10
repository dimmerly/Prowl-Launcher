using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Launcher;

static class ModalLayout
{
    internal static ModalBuilder CenteredContent(this ModalBuilder builder, Action<Paper> draw) =>
        builder.Content(p =>
        {
            // Origami invokes content inside the dialog body. Center its enclosing window
            // with equal flexible margins, using its actual height rather than an estimate.
            ElementHandle window = p.CurrentParent.GetParentHandle();
            new ElementBuilder(p, window).Top(UnitValue.Stretch()).Bottom(UnitValue.Stretch());
            draw(p);
        });
}
