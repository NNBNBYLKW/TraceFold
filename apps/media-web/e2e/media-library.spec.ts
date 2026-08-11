import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { appendFile, copyFile, mkdir, readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";


const webRoot = path.resolve(import.meta.dirname, "..");
const apiRoot = path.resolve(webRoot, "..", "media-api");
const fixtureRoot = path.join(apiRoot, "tests", "fixtures", "media");
const dataRoot = path.join(webRoot, ".e2e-data");
const inboxDir = path.join(dataRoot, "inbox");
const libraryDir = path.join(dataRoot, "library");

test.describe.configure({ mode: "serial" });

async function copyFixtureToInbox(fixture: string, targetName = fixture) {
  await mkdir(inboxDir, { recursive: true });
  await copyFile(path.join(fixtureRoot, fixture), path.join(inboxDir, targetName));
}

async function copyImageVariantToInbox(targetName: string, marker: string) {
  await copyFixtureToInbox("photo.jpg", targetName);
  await appendFile(path.join(inboxDir, targetName), `\ntracefold-e2e:${marker}`);
}

async function pathExists(target: string): Promise<boolean> {
  try {
    await stat(target);
    return true;
  } catch {
    return false;
  }
}

async function createAndSelectTaxonomy(page: Page, label: string, name: string) {
  await page.getByLabel(`搜索或创建${label}`, { exact: true }).fill(name);
  await page.getByRole("button", { name: `创建“${name}”` }).click();
  if (label !== "系列") {
    await expect(page.getByRole("checkbox", { name })).toBeChecked();
  } else {
    await expect(page.getByLabel("系列", { exact: true })).toHaveValue(/.+/);
  }
}

async function scanUntilVisible(page: Page, filename: string) {
  await page.goto("/inbox");
  const card = page.getByRole("button", { name: `打开 ${filename}` });
  for (let attempt = 0; attempt < 3 && await card.count() === 0; attempt += 1) {
    await page.getByRole("button", { name: "手动刷新" }).click();
  }
  await expect(card).toBeVisible();
  return card;
}

async function ingestReadyItem(page: Page, filename: string, displayName: string) {
  const card = await scanUntilVisible(page, filename);
  await card.click();
  await expect(page.getByRole("button", { name: "正式入库" })).toBeVisible();
  await page.getByLabel("显示名称").fill(displayName);
  await page.getByRole("button", { name: "正式入库" }).click();
  await expect(page.getByRole("button", { name: `打开 ${filename}` })).toHaveCount(0);
}

test("scans, tags, ingests, and finds an image", async ({ page }) => {
  await copyFixtureToInbox("photo.jpg");
  await page.goto("/inbox");
  await page.getByRole("button", { name: "手动刷新" }).click();
  await page.getByRole("button", { name: "手动刷新" }).click();
  await page.getByRole("button", { name: "打开 photo.jpg" }).click();
  await page.getByLabel("显示名称").fill("杭州夜景");
  await createAndSelectTaxonomy(page, "作者", "林同学");
  await createAndSelectTaxonomy(page, "系列", "城市漫游");
  await createAndSelectTaxonomy(page, "内容标签", "夜景");
  await page.getByRole("button", { name: "正式入库" }).click();

  await expect(page.getByRole("button", { name: "打开 photo.jpg" })).toHaveCount(0);
  await page.getByRole("link", { name: /文件库/ }).click();
  await expect(page.getByText("杭州夜景")).toBeVisible();
  await expect.poll(() => pathExists(path.join(inboxDir, "photo.jpg"))).toBe(false);
  await expect.poll(() => pathExists(path.join(libraryDir, "photo.jpg"))).toBe(true);
});

test("merges an MP4 and M4A pair before ingesting one formal video", async ({ page }) => {
  await copyFixtureToInbox("paired.mp4", "pair-flow.mp4");
  await copyFixtureToInbox("paired.m4a", "pair-flow.m4a");

  const card = await scanUntilVisible(page, "pair-flow.mp4");
  await card.click();
  await expect(page.getByRole("button", { name: "正式入库" })).toBeVisible({ timeout: 30_000 });
  await page.getByLabel("显示名称").fill("合并后的视频");
  await page.getByRole("button", { name: "正式入库" }).click();

  await expect(page.getByRole("button", { name: "打开 pair-flow.mp4" })).toHaveCount(0);
  await expect.poll(() => pathExists(path.join(inboxDir, "pair-flow.mp4"))).toBe(false);
  await expect.poll(() => pathExists(path.join(inboxDir, "pair-flow.m4a"))).toBe(false);
  await expect.poll(() => pathExists(path.join(libraryDir, "pair-flow.mp4"))).toBe(true);
  await expect.poll(() => pathExists(path.join(libraryDir, "pair-flow.m4a"))).toBe(false);
  const formalPairFiles = (await readdir(libraryDir)).filter((name) => name.startsWith("pair-flow"));
  expect(formalPairFiles).toEqual(["pair-flow.mp4"]);
});

test("shows both previews and keeps both files on a registered name conflict", async ({ page }) => {
  await copyImageVariantToInbox("keep-both.jpg", "keep-original");
  await ingestReadyItem(page, "keep-both.jpg", "保留原文件");
  await copyImageVariantToInbox("keep-both.jpg", "keep-incoming");

  const card = await scanUntilVisible(page, "keep-both.jpg");
  await card.click();
  await page.getByLabel("显示名称").fill("保留新文件");
  await page.getByRole("button", { name: "正式入库" }).click();

  const dialog = page.getByRole("dialog", { name: "目标位置已有同名或相同内容的文件" });
  await expect(dialog.getByRole("img", { name: "文件库中的文件" })).toBeVisible();
  await expect(dialog.getByRole("img", { name: "本次待入库文件" })).toBeVisible();
  await dialog.getByRole("button", { name: "两者都保留" }).click();

  await expect(dialog).toHaveCount(0);
  await expect.poll(() => pathExists(path.join(inboxDir, "keep-both.jpg"))).toBe(false);
  await expect.poll(() => pathExists(path.join(libraryDir, "keep-both.jpg"))).toBe(true);
  await expect.poll(() => pathExists(path.join(libraryDir, "keep-both (2).jpg"))).toBe(true);
});

test("leaves the incoming source in the inbox when a conflict is skipped", async ({ page }) => {
  await copyImageVariantToInbox("skip.jpg", "skip-original");
  await ingestReadyItem(page, "skip.jpg", "跳过前的正式文件");
  await copyImageVariantToInbox("skip.jpg", "skip-incoming");

  const card = await scanUntilVisible(page, "skip.jpg");
  await card.click();
  await page.getByLabel("显示名称").fill("被跳过的文件");
  await page.getByRole("button", { name: "正式入库" }).click();
  const dialog = page.getByRole("dialog", { name: "目标位置已有同名或相同内容的文件" });
  await dialog.getByRole("button", { name: "跳过" }).click();

  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("button", { name: "打开 skip.jpg" })).toBeVisible();
  await expect.poll(() => pathExists(path.join(inboxDir, "skip.jpg"))).toBe(true);
  await expect.poll(() => pathExists(path.join(libraryDir, "skip.jpg"))).toBe(true);
  await expect.poll(async () => (await readdir(libraryDir)).filter((name) => name.startsWith("skip")).length).toBe(1);
});

test("requires a second confirmation and replaces the registered asset and metadata", async ({ page }) => {
  await copyImageVariantToInbox("replace.jpg", "replace-original");
  await ingestReadyItem(page, "replace.jpg", "替换前资料");
  await copyImageVariantToInbox("replace.jpg", "replace-incoming");

  const card = await scanUntilVisible(page, "replace.jpg");
  await card.click();
  await page.getByLabel("显示名称").fill("替换后资料");
  await page.getByRole("button", { name: "正式入库" }).click();
  const dialog = page.getByRole("dialog", { name: "目标位置已有同名或相同内容的文件" });
  await dialog.getByRole("button", { name: "替换原文件" }).click();
  await expect(dialog.getByText("现有文件及其资料将被本次文件替换。此操作无法撤销。")).toBeVisible();
  await dialog.getByRole("button", { name: "确认替换" }).click();

  await expect(dialog).toHaveCount(0);
  await page.goto("/library");
  await expect(page.getByText("替换后资料")).toBeVisible();
  await expect(page.getByText("替换前资料")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "打开 replace.jpg" })).toHaveCount(1);
  const formalBytes = await readFile(path.join(libraryDir, "replace.jpg"), "utf8");
  expect(formalBytes).toContain("tracefold-e2e:replace-incoming");
});

test("isolates an invalid processor input while another image still ingests", async ({ page }) => {
  await copyFixtureToInbox("paired.mp4", "invalid-pair.mp4");
  await copyFixtureToInbox("photo.jpg", "invalid-pair.m4a");
  await copyImageVariantToInbox("healthy-after-failure.jpg", "healthy-after-failure");

  const failedCardButton = await scanUntilVisible(page, "invalid-pair.mp4");
  const failedCard = failedCardButton.locator("..");
  await expect(failedCard).toHaveClass(/status-failed/, { timeout: 30_000 });
  await ingestReadyItem(page, "healthy-after-failure.jpg", "不受失败影响的图片");

  await expect(page.getByRole("button", { name: "打开 invalid-pair.mp4" })).toBeVisible();
  await expect.poll(() => pathExists(path.join(inboxDir, "invalid-pair.mp4"))).toBe(true);
  await expect.poll(() => pathExists(path.join(inboxDir, "invalid-pair.m4a"))).toBe(true);
  await expect.poll(() => pathExists(path.join(libraryDir, "healthy-after-failure.jpg"))).toBe(true);
});
