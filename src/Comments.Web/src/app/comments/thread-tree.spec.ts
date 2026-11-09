import { CommentDto } from '../core/models';
import { buildThreadTree } from './thread-tree';

function reply(id: number, parentId: number): CommentDto {
  return {
    id,
    parentId,
    rootId: 1,
    depth: 1,
    userName: 'U',
    email: 'u@example.com',
    homePage: null,
    html: 'x',
    createdAt: '2026-01-01T00:00:00Z',
    replyCount: 0,
    attachment: null,
  };
}

describe('buildThreadTree', () => {
  it('nests replies under their parents at any depth', () => {
    const tree = buildThreadTree(1, [reply(2, 1), reply(3, 2), reply(4, 1), reply(5, 3)]);

    expect(tree.map((n) => n.comment.id)).toEqual([2, 4]);
    expect(tree[0].children[0].comment.id).toBe(3);
    expect(tree[0].children[0].children[0].comment.id).toBe(5);
  });

  it('keeps replies whose parent is not loaded at the top level', () => {
    const tree = buildThreadTree(1, [reply(7, 6)]);

    expect(tree.map((n) => n.comment.id)).toEqual([7]);
  });

  it('orders by id even if the input is not', () => {
    const tree = buildThreadTree(1, [reply(4, 2), reply(2, 1)]);

    expect(tree[0].comment.id).toBe(2);
    expect(tree[0].children[0].comment.id).toBe(4);
  });
});
