# Under New Management

Every diner, café and bar in the city has a name over the door and somebody counting the takings. Now that somebody can be you. Buy a business from its owner, keep the staff on, and make your money from the real citizens who walk in and order.

**This is the first release.** It covers the places citizens go to eat and drink, which are the businesses they visit often enough to run. More kinds of business are planned (see *What's next*).

## Buying a business

Talk to the **owner** of a diner, café, restaurant or bar and choose **"I'd like to buy your business."** The asking price is shown on the option, and it's greyed out if you can't afford it. If the owner isn't around, any **staff member on shift** can phone them for you.

The price depends on:
- **Regular customers.** These are the citizens whose favourite place it is for a meal, a snack, coffee or a drink. A busy place costs more than a quiet one.
- **The neighbourhood.** The same diner costs more uptown than in the slums.
- **The menu.** Places with pricier menus cost more, and earn more.

The **owner stays on as your manager**, and the staff keep their jobs. Nothing about the place changes except who gets the money. You also get:
- **Keys** to every door.
- **The owner's login** for the office cruncher.
- **The run of the place.** No trespassing, no "buy something or leave", and no muggers bothering you inside.

## Running it

Every sale is real. When a citizen orders a burger at your diner, the money goes into **the till** and a burger comes out of **your stock**.

- **Stock.** You start with 20 of each menu item. When something runs out, customers can't order it, and when nothing they want is left, they walk out. Those are counted as *turned away*.
- **Orders.** Restock on the cruncher, 10 at a time. You pay up front (30% of the menu price, from the till first, then your wallet), and the delivery arrives in 2 to 6 days.
- **Wages.** At midnight your staff are paid out of the till. If the till is short, the rest comes from your wallet.
- **Selling.** Changed your mind? Tell the manager, or staff on shift, that you want to sell. You get back 60% of what you paid, plus whatever's in the till.

## Making it yours

- **Decorate.** Inside a business you own, the **Edit Decor** button works just like at home (sandbox games, or the story once you have your apartment). Repaint walls, floors and ceilings room by room, and buy and place your own furniture.
- **The fittings stay.** Counters, registers, tables, seats, menu boards and the rest are what your staff and customers use, so they can't be moved, stored or sold. Litter, junk and cardboard boxes can be cleared out.
- **Lit ceilings.** Some places, like diners, have glowing ceiling panels. Paint one and it glows in the Multiply colour you choose; pick black to switch the glow off.
- **The staff know who you are.** Waiters, bar staff and counter staff greet you as their boss. Not enthusiastically.

Shared spaces next door, like a building's public bathrooms, aren't part of your business and can't be decorated.

## The Business Ledger

A new program on the cruncher, in two places:

**At your business:** log in on the office cruncher (your login fills itself in) and open **Business Ledger**.
- **The till:** takings, customers in the last 24 hours, customers turned away, and the wage bill. **COLLECT** puts the whole till in your wallet.
- **Every menu item:** stock, price, what it costs you, and orders on the way. **ORDER 10** restocks it.
- **Recent sales:** every customer by name, with what they bought and when.

**At home:** put a cruncher in your apartment (Apartment Decor › Furnishings) and the ledger opens on **My Businesses**, a list of everything you own with tills, sales, low-stock warnings and orders due.
- **OPEN** a business to manage it remotely: check stock, place orders, read the sales.
- **TRANSFER** sends the till home by courier. It arrives at 8 the next morning, less a 10% fee. Collecting in person gets you the full amount, so a visit still pays.

## What's next

Planned for future versions:
- **Shops.** Pawn shops, hardware stores and other retail. The game has no errand that sends citizens shopping there, so they need new shoppers first.
- **Supermarkets.** Grocery runs are rare in the game, so they need a different kind of deal to be worth owning.
- **Deliveries you can see** arriving at the door.
- **A full till as a target.** Leave too much cash in the register and someone may come for it.

## Bugs and feedback

**Tell us what you think!** Found a bug, a business that's priced wrong, or a place that never gets customers? Want a certain kind of business next? Leave a comment on this page or reply to our Discord post. Feedback decides what gets built next. For bugs, it helps to include your `BepInEx/LogOutput.log`.

## Configuration

After the first launch, edit `BepInEx/config/sodmods.proprietor.cfg` (or use r2modman's config editor). Highlights:

| Setting | Default | Description |
|---|---|---|
| `Buying.BasePrice` | 1500 | Price of a business before its customers are counted |
| `Buying.PricePerRegular` | 12 | Added for each regular customer |
| `Buying.SellBackShare` | 0.6 | Share of the price you get back when selling |
| `Stock.StartingStock` | 20 | Stock of each item when you buy a business |
| `Stock.OrderSize` | 10 | Items per order |
| `Stock.WholesaleShare` | 0.3 | What stock costs you, as a share of the menu price |
| `Stock.MinDeliveryDays` / `MaxDeliveryDays` | 2 / 6 | How long orders take |
| `Running.WagePerStaff` | 8 | Daily wage per member of staff |
| `Remote.TransferFee` | 0.1 | The courier's cut when you transfer from home (0 = free) |

## Safe to remove?

**Yes.** What you own is kept in a small file next to each save, not in the save itself. Without the mod your businesses simply go back to their owners. Money earned or spent stays, and the keys you were given stay on your keyring. Decorating you've done stays too (the game saves it with the rooms), and lit ceilings you painted go back to glowing white.

## Compatibility

- Built for the main (IL2CPP) branch of the game. Requires **SOD.Common**.
- Works alongside The Protectorate's other mods (Detective License, Sanitation Department, Margin City and the rest).

---

*Made with the help of AI (Claude, by Anthropic). Designed, play-tested and released by The Protectorate.*
