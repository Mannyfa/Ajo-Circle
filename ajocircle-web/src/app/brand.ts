/**
 * Working product name and public-site content that the owner may change without touching components.
 * Keep all display branding here — "AjoCircle" is a working name.
 */
export const brand = {
  name: 'AjoCircle',
  tagline: 'Your ajo groups. Your people. Everything organized.',
  supportEmail: 'support@example.com',
  /** Social links appear in the footer only when configured. Leave empty to hide the Connect column. */
  social: [] as { label: string; href: string }[],
  /**
   * Pricing has not been decided. The page says so instead of inventing a price.
   * Set `plans` once pricing is approved; each renders as a card.
   */
  pricing: {
    announced: false,
    note: 'Pricing has not been set yet. We will publish it here before any paid plan starts, and existing groups will be told in advance.',
    plans: [] as { name: string; price: string; description: string; features: string[] }[],
  },
  /** Legal pages show a "pending review" notice until qualified counsel signs them off. */
  legalReviewed: false,
} as const
