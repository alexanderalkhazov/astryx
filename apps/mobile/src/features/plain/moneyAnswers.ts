/** T-192: short, true answers beside every money action, in Simple mode. Keep them in step with what the server does. */
export type MoneyAction = 'approve' | 'close' | 'invest' | 'rebalance';

export const WHAT_IF: Record<MoneyAction, readonly { q: string; a: string }[]> = {
  approve: [
    {
      q: 'What if the price falls?',
      a: 'If it falls to the stop, the shares sell automatically. You lose about the max loss shown above, a little more if the price jumps past the stop overnight.',
    },
    {
      q: 'What if I close the app?',
      a: 'Nothing changes. The order keeps working on the server. A buy-stop waits for its price and ends at today’s close if it isn’t reached.',
    },
    {
      q: 'Can I undo it?',
      a: 'Until it fills, you can cancel it in Activity. After that you can close the position at any time while the market is open.',
    },
  ],
  close: [
    { q: 'What happens when I close?', a: 'All the shares sell at about the current price, and the result moves into your cash.' },
    { q: 'What if the price goes up afterwards?', a: 'Later moves no longer affect you. You can buy again later if you change your mind.' },
    { q: 'Can I undo it?', a: 'No. A sale can’t be reversed, but it’s practice money, so you can always try again.' },
  ],
  invest: [
    { q: 'What happens when I invest?', a: 'Your cash buys the funds in your plan’s mix straight away, at the latest prices.' },
    {
      q: 'What if prices fall?',
      a: 'The value of your funds falls too. Broad funds go up and down, and plans like this are meant to be held for years.',
    },
    { q: 'Can I undo it?', a: 'Not directly, but you can change your plan’s mix at any time and rebalance.' },
  ],
  rebalance: [
    {
      q: 'What does rebalancing do?',
      a: 'It sells a little of the funds that grew past your mix and buys the ones that fell behind, so you’re back to the mix you chose.',
    },
    { q: 'Does it cost anything?', a: 'Not here. It uses practice money and there are no fees.' },
    { q: 'Can I undo it?', a: 'No, but you can change your mix and rebalance again whenever you like.' },
  ],
};

export const SAFETY_PROMISE = 'Practice money: nothing here can cost you real money';
