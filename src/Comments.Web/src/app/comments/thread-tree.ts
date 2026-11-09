import { CommentDto } from '../core/models';

export interface ThreadNode {
  comment: CommentDto;
  children: ThreadNode[];
}

/**
 * Builds the reply tree of one thread from the flat list the API returns (id order, parents first).
 * A reply whose parent is not loaded yet is attached to the root level rather than dropped.
 */
export function buildThreadTree(rootId: number, replies: readonly CommentDto[]): ThreadNode[] {
  const nodes = new Map<number, ThreadNode>();
  const roots: ThreadNode[] = [];

  for (const comment of [...replies].sort((a, b) => a.id - b.id)) {
    const node: ThreadNode = { comment, children: [] };
    nodes.set(comment.id, node);
    const parent = comment.parentId === rootId ? undefined : nodes.get(comment.parentId ?? -1);
    (parent?.children ?? roots).push(node);
  }

  return roots;
}
