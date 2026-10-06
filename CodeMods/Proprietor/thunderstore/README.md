# Under New Management

Every diner, café and bar in the city has a name over the door and somebody counting the takings. Now that somebody can be you. Buy a business from its owner, keep the staff on, and make your money from the real citizens who walk in and order.

**Right now it covers the places citizens go to eat and drink,** which are the businesses they visit often enough to run. More kinds of business are planned (see *What's next*).

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

- **Stock.** A new business comes with a couple of orders' worth of each menu item. When something runs out, customers can't order it, and when nothing they want is left, they walk out. Those are counted as *turned away*.
- **Orders.** Restock on the cruncher. Order sizes follow how busy the place is (a few days of that item's expected sales). You pay up front (30% of the menu price, from the till first, then your wallet), and the delivery arrives in 1 to 3 days. Press ORDER again for more.
- **Wages.** At midnight the staff are paid out of the till (vacant positions cost nothing). If the till is short, the rest comes from your wallet.
- **A manager who restocks.** Tell the manager (or have staff on shift call them) *"I'll give you a raise if you handle the restocking."* The raise is paid with the wages, and an hour before closing (11pm for places that never close) the manager tops up anything running low, by what it actually sold lately, paid from the day's takings. They never spend the money set aside for wages. Changed your mind? Tell them they can go back to their old pay.
- **The manager's report.** Every morning at 7, each manager vmails you: last night's wages, deliveries, what they ordered, what's running low or out, and anything they've noticed about the customers. Read it on your home cruncher. One message lets you know the reports are in.
- **Regulars.** Every citizen has a favourite place for a meal, a snack, coffee and a drink. Turn a regular away with empty shelves and they may start going somewhere else; keep the place stocked and newcomers may make it their favourite. Nobody announces it, but a good manager notices.
- **Business doesn't stop when you leave.** The game simulates citizens across town less than the ones around you, so a business you're far from would sell a fraction of what it sells with you nearby. The mod makes up the difference from the business's own regulars (using its real stock), so your businesses earn the same wherever you are.
- **Opening hours.** Businesses keep their own hours; a late-night bar or eatery may not open until midday. The ledger tells you whether a place is open and who's in.
- **After hours.** You can stay inside your own business at closing time: the staff lock up and go home around you (and if they've already left, the place is closed up for them). Waiting with your watch isn't interrupted.
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
- **The till:** whether the place is open and how many staff are in, the last 24 hours' sales, customers turned away, and the wage bill. **COLLECT** puts the till in your wallet, leaving a day's wages behind so the business keeps paying its staff.
- **Put money in:** **DEPOSIT** moves cash from your wallet into the till, ¢100 a press, to carry a business through a slow patch.
- **Every menu item:** stock, price, what it costs you, and orders on the way. **ORDER** restocks it.
- **Recent sales:** every customer by name, with what they bought and when.

**At home:** put a cruncher in your apartment (Apartment Decor › Furnishings) and the ledger opens on **My Businesses**, a list of everything you own with tills, sales, low-stock warnings and orders due.
- **OPEN** a business to manage it remotely: check stock, place orders, put money in, read the sales.
- **TRANSFER** sends the till (less a day's wages) home by courier. It arrives at 8 the next morning, less a 10% fee. Collecting in person gets you the full amount, so a visit still pays.

## What's next

Planned for future versions:
- **Shops.** Pawn shops, hardware stores and other retail. The game has no errand that sends citizens shopping there, so they need new shoppers first.
- **Supermarkets.** Grocery runs are rare in the game, so they need a different kind of deal to be worth owning.
- **Deliveries you can see:** crates turning up in the stockroom or back room.
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
| `Stock.DemandPerRegular` | 0.45 | Daily sales per regular customer; order sizes and off-screen trade follow it |
| `Stock.DaysOfStock` | 3 | Days of sales one order (or a manager's restock) covers |
| `Stock.MinOrder` / `MaxOrder` | 10 / 100 | Smallest and largest order |
| `Stock.WholesaleShare` | 0.3 | What stock costs you, as a share of the menu price |
| `Stock.MinDeliveryDays` / `MaxDeliveryDays` | 1 / 3 | How long orders take |
| `Running.WagePerStaff` | 8 | Daily wage per member of staff |
| `Running.ManagerRaise` | 25 | Daily raise for a manager who handles restocking |
| `Running.KeepFloat` | true | COLLECT and TRANSFER leave a day's wages in the till |
| `Regulars.LoseChance` | 0.33 | Chance a regular turned away leaves for a rival |
| `Regulars.WinChance` | 0.05 | Chance a new customer becomes a regular (if nobody was turned away that day) |
| `Running.DepositStep` | 100 | How much each DEPOSIT press puts in the till |
| `Remote.TransferFee` | 0.1 | The courier's cut when you transfer from home (0 = free) |

## Safe to remove?

**Yes.** What you own is kept in small files next to each save, not in the save itself. Without the mod your businesses simply go back to their owners, citizens go back to their original favourite places, and the managers' reports disappear from your vmail (they're never written into the save). Money earned or spent stays, and the keys you were given stay on your keyring. Decorating you've done stays too (the game saves it with the rooms), and lit ceilings you painted go back to glowing white.

## Compatibility

- Built for the main (IL2CPP) branch of the game. Requires **SOD.Common**.
- Designed to sit alongside other cruncher-app mods such as Stock Market, and to follow Life and Living's economy changes (checked against their code, not yet played together).
- Works alongside The Protectorate's other mods (Detective License, Sanitation Department, Margin City and the rest).

---

*Made with the help of AI (Claude, by Anthropic). Designed, play-tested and released by The Protectorate.*
