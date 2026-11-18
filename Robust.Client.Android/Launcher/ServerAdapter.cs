using Android.Content;
using Android.Views;
using Android.Widget;

namespace Robust.Client.Android.Launcher;

/// <param name="Players">-1 = unknown yet, -2 = offline.</param>
public sealed record ServerItem(string Address, string Name, int Players, int MaxPlayers);

internal sealed class ServerAdapter(Context ctx) : BaseAdapter<ServerItem>
{
    private List<ServerItem> _items = new();

    public void SetItems(List<ServerItem> items)
    {
        _items = items;
        NotifyDataSetChanged();
    }

    public override ServerItem this[int position] => _items[position];
    public override int Count => _items.Count;
    public override long GetItemId(int position) => position;

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var item = _items[position];
        var row = Ui.Column(ctx, 12);
        row.Background = Ui.Rounded(ctx, Ui.Panel);

        var top = Ui.Row(ctx);
        var name = Ui.Label(ctx, item.Name, 16, bold: true);
        name.SetMaxLines(2);
        top.AddView(name, Ui.Fill(1));
        var players = item.Players switch
        {
            -1 => Ui.Label(ctx, "…", 14, Ui.Muted),
            -2 => Ui.Label(ctx, "офлайн", 14, Ui.Bad),
            _ => Ui.Label(ctx, $"{item.Players}/{item.MaxPlayers}", 14, item.Players > 0 ? Ui.Good : Ui.Muted),
        };
        top.AddView(players, Ui.Wrap(8, ctx));
        row.AddView(top);
        row.AddView(Ui.Label(ctx, item.Address, 12, Ui.Muted));
        return row;
    }
}
