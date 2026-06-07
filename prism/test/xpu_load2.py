import torch, time, os
print(f"XPU available: {torch.xpu.is_available()}, devices: {torch.xpu.device_count()}")
for i in range(torch.xpu.device_count()):
    props = torch.xpu.get_device_properties(i)
    print(f"Device {i}: {props.name}, memory: {props.total_memory / 1024**3:.1f} GB")

# Allocate memory and compute
print("Allocating 2GB and computing...")
a = torch.randn(16384, 16384, device='xpu')
print(f"Allocated: {a.element_size() * a.numel() / 1024**3:.2f} GB")

for i in range(20):
    b = torch.randn(16384, 16384, device='xpu')
    c = a @ b
    torch.xpu.synchronize()
    del b, c
    if i % 5 == 0:
        print(f"Iteration {i+1}/20, mem: {torch.xpu.memory_allocated() / 1024**3:.2f} GB")
    time.sleep(1)

print("Freeing memory...")
del a
torch.xpu.empty_cache()
time.sleep(3)
print("DONE")
