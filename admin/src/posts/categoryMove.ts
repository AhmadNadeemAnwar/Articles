/** Small rules for moving posts between categories from the Posts list. No React, so they can be unit tested. */

/** What the list's category filter sends for "posts in no category". */
export const NO_CATEGORY = 'none'

/** The filter's value, as the API wants it: nothing for "all", otherwise "none" or a category id. */
export function categoryParam(filter: string): string | undefined {
  return filter === '' ? undefined : filter
}

/** A row's category dropdown holds an id, or an empty string for "No category"; the API wants null for none. */
export function categoryChoice(value: string): string | null {
  return value === '' ? null : value
}

/** The line shown after a post is moved. `name` is null when the post was taken out of its category. */
export function movedMessage(title: string, name: string | null): string {
  return name === null ? `“${title}” is now in no category.` : `“${title}” moved to ${name}.`
}

/** Whether any filter narrows the list, so an empty list can say "no match" rather than "no posts yet". */
export function isFiltering(filters: { status: string; term: string; category: string }): boolean {
  return filters.status !== '' || filters.term !== '' || filters.category !== ''
}
