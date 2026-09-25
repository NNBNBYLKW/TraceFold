import { render, screen } from "@testing-library/react";

import { MediaPreview } from "../src/shared/components/MediaPreview";


test("requests previews with CORS so an allowed localhost frontend can load them", () => {
  render(<MediaPreview src="/api/inbox/items/item-1/preview" alt="测试图片" mediaType="image" />);

  expect(screen.getByRole("img", { name: "测试图片" })).toHaveAttribute(
    "crossorigin",
    "anonymous",
  );
});
