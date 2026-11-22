fn main() {
    let list_checksum = run_linked_list_benchmark();
    let sort_checksum = run_quick_sort_benchmark();
    let simd_checksum = run_saxpy_benchmark();

    println!("list={list_checksum}");
    println!("sort={sort_checksum}");
    println!("simd={simd_checksum}");
}

fn run_linked_list_benchmark() -> i64 {
    let mut checksum: i64 = 0;
    for round in 0..400i32 {
        let mut list = List { head: None };
        for i in 0..5000i32 {
            list.head = Some(Node::new(i.wrapping_mul(31) ^ round, list.head.take()));
        }
        if let Some(top) = list.head.as_deref_mut() {
            top.add(round);
        }
        checksum += sum_list(&list.head);
    }
    checksum
}

fn sum_list(head: &Option<Box<Node>>) -> i64 {
    let mut total: i64 = 0;
    let mut cur = head.as_deref();
    while let Some(node) = cur {
        total += node.value as i64;
        cur = node.next.as_deref();
    }
    total
}

fn run_quick_sort_benchmark() -> i64 {
    let mut scratch = vec![0i32; 4096];
    let mut stack = vec![0i32; 128];

    let mut checksum: i64 = 0;
    for run in 0..400i32 {
        let data = scratch.as_mut_slice();
        fill_pseudo_random(data, run);
        quick_sort(data, stack.as_mut_slice());
        checksum += data[0].wrapping_add(data[2048]).wrapping_add(data[4095]) as i64;
    }
    checksum
}

fn fill_pseudo_random(data: &mut [i32], seed_index: i32) {
    let mut state: u32 = 0x9E3779B9u32.wrapping_add((seed_index as u32).wrapping_mul(7919u32));
    for slot in data.iter_mut() {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        *slot = (state & 0x7FFFFFFFu32) as i32;
    }
}

fn quick_sort(data: &mut [i32], stack: &mut [i32]) {
    if data.len() <= 1 {
        return;
    }

    let mut sp: usize = 0;
    stack[sp] = 0;
    stack[sp + 1] = (data.len() as i32) - 1;
    sp += 2;

    while sp > 0 {
        sp -= 2;
        let mut low = stack[sp];
        let mut high = stack[sp + 1];

        while low < high {
            let (left_end, right_start) = partition(data, low, high);
            if left_end - low < high - right_start {
                if right_start < high {
                    stack[sp] = right_start;
                    stack[sp + 1] = high;
                    sp += 2;
                }
                high = left_end;
            } else {
                if low < left_end {
                    stack[sp] = low;
                    stack[sp + 1] = left_end;
                    sp += 2;
                }
                low = right_start;
            }
        }
    }
}

fn partition(data: &mut [i32], low: i32, high: i32) -> (i32, i32) {
    let pivot = data[(low + ((high - low) >> 1)) as usize];
    let mut i = low;
    let mut j = high;

    while i <= j {
        while data[i as usize] < pivot {
            i += 1;
        }
        while data[j as usize] > pivot {
            j -= 1;
        }
        if i <= j {
            data.swap(i as usize, j as usize);
            i += 1;
            j -= 1;
        }
    }
    (j, i)
}

fn run_saxpy_benchmark() -> i64 {
    let mut y_vec = Vec::with_capacity(2048);
    let mut x_vec = Vec::with_capacity(2048);
    for i in 0..2048i32 {
        y_vec.push(((i & 15) as f32) * 0.25f32);
        x_vec.push((((i * 3) & 31) as f32) * 0.125f32);
    }

    for _ in 0..250000 {
        saxpy(y_vec.as_mut_slice(), x_vec.as_slice(), 0.0001f32);
    }

    ((y_vec[0] * 100.0f32) as i64)
        + ((y_vec[1024] * 100.0f32) as i64)
        + ((y_vec[2047] * 100.0f32) as i64)
}

fn saxpy(y: &mut [f32], x: &[f32], a: f32) {
    let xs = &x[..y.len()];
    for i in 0..y.len() {
        y[i] += a * xs[i];
    }
}

struct Node {
    value: i32,
    next: Option<Box<Node>>,
}

impl Node {
    fn new(value: i32, next: Option<Box<Node>>) -> Box<Node> {
        Box::new(Node { value, next })
    }

    fn add(&mut self, delta: i32) {
        self.value = self.value.wrapping_add(delta);
    }
}

struct List {
    head: Option<Box<Node>>,
}

impl Drop for List {
    fn drop(&mut self) {
        let mut cur = self.head.take();
        while let Some(mut node) = cur {
            cur = node.next.take();
        }
    }
}
