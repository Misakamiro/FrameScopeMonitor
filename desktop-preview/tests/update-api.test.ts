import { describe,it,expect,vi } from "vitest";
const native=vi.hoisted(()=>vi.fn());
vi.mock("../src/preview/runtime/platform",()=>({nativeOperation:native,isDesktop:()=>true}));
import { updateAction } from "../src/lib/update-api";
describe("native update boundary",()=>{
  it("sends release identity only for download and installation",async()=>{
    await updateAction("update_download",{release_id:42});
    expect(native).toHaveBeenLastCalledWith("update_download",{release_id:42});
    await expect(updateAction("update_install",{release_id:42,url:"https://evil"})).rejects.toThrow();
    await expect(updateAction("update_install",{path:"C:/evil.exe"})).rejects.toThrow();
  });
  it("does not accept paths for recovery",async()=>{
    await expect(updateAction("update_restore",{path:"C:/other"})).rejects.toThrow();
    await updateAction("update_restore");
    expect(native).toHaveBeenLastCalledWith("update_restore",{});
  });
});
