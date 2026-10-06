# Changelog

## 1.3.0

- **Managers report by vmail.** Each morning every manager vmails you a report (wages, deliveries, restocking, what's low or out, how the customers seem), replacing the pile of pop-ups at midnight and 7am. One message tells you the reports are in. The last week of reports is kept.
- **Regulars come and go.** A regular turned away by empty shelves may switch to a rival; new customers may become regulars at a well-stocked place. It all happens quietly, the way it would for a real owner.
- **Businesses earn the same wherever you are.** The game simulates distant citizens less, so businesses far from you were selling a fraction of what they sell with you nearby. The mod now makes up the difference from each business's own regulars and stock. Expect a lot more in the till.
- **Restocking happens before closing,** from the day's takings, instead of first thing in the morning. The manager never spends the wage money.
- **COLLECT and TRANSFER leave a day's wages in the till,** so a business keeps paying its staff while you take the profit (`Running.KeepFloat`).
- **Rebalanced:** the manager's raise is now ¢25 a day (was ¢50), and demand per regular is 0.45 (was 0.25), measured in play. Config files still on the old defaults are updated automatically; values you changed yourself are kept.
- Item names in reports are now plural ("out of Donuts").

## 1.2.0

- **Give the manager a raise to handle restocking.** Offer it in conversation (in person, or by phone through staff on shift). The raise is paid with the wages, and every morning at 7 the manager orders whatever is running low, paid from the till.
- **Morning stock report.** Each morning you're told about any business that's out of stock or running low, and what the manager ordered.
- **Stock scales with the business.** Order sizes and starting stock follow how many regulars a place has, so busy places order more. A manager on a raise restocks by what each item actually sold over the last few days. Deliveries now take 1 to 3 days.
- **Put money into the till.** A new ledger row lets you move cash from your wallet into a business, to cover wages and restocking through a slow patch.
- **Open or closed at a glance.** The ledger shows whether a business is open right now and how many staff are in.
- **Staying in your own business after hours works properly.** Your staff now lock up and go home at closing time even when you're inside (before, they waited for you to leave, so the place never properly closed and the next day started late). If the staff have already gone, the business is closed up for them: lights off, doors locked. Waiting with your watch is no longer cut short at closing time.
- **Fixes:** vacant jobs are no longer paid wages; wages and deliveries now run on the game clock (via SOD.Common); the Business Ledger no longer disturbs other mods' cruncher apps (such as Stock Market).

## 1.1.0

- **Decorate your businesses.** The Edit Decor button now works inside businesses you own: repaint walls, floors and ceilings, and buy and place your own furniture. The business's own fittings (counters, registers, tables, menu boards...) stay where they are, but litter, junk and cardboard boxes can be cleared out.
- **Lit ceilings take your colour.** Diner-style glowing ceilings now glow in the Multiply colour you paint them (black for no glow), instead of turning white again when the lights come on.
- **Staff call you boss.** Waiters, bar staff and counter staff at your businesses greet you as their boss ("Morning, boss.").
- Removed a research setting that was left in 1.0.

## 1.0.2

- Added a note that the mod is made with the help of AI. No changes to the mod itself.

## 1.0.1

- Added a "Safe to remove?" note to the description and a link to the source code. No changes to the mod itself.

## 1.0.0

First release.

- Buy diners, cafés, restaurants and bars from their owner, or through staff on shift. The price is based on regular customers, neighbourhood and menu.
- Real customers pay into the till and use up stock. Customers are turned away when the shelves are bare.
- The Business Ledger program at your business: collect the till, order stock (delivered in 2 to 6 days), and see every sale.
- The Business Ledger at home: every business you own in one list, with remote orders and courier transfers.
- Daily wages, selling back, keys and login handed over, and no trespassing, loitering or muggings in your own business.
