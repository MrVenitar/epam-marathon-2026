#!/usr/bin/env python3
"""Fill a fresh deployment with demo data: locations, dishes, tables and (optionally) a waiter.

Usage:
    API_URL=https://<api-id>.execute-api.us-west-2.amazonaws.com/develop python scripts/seed.py

Optional:
    WAITER_EMAIL / WAITER_PASSWORD  - create a waiter account assigned to the first location
    SEED_FORCE=1                    - seed even if locations already exist

Idempotent: if the API already returns locations, nothing is created (unless SEED_FORCE=1).
Images are taken from the frontend assets and downscaled with Pillow when it is installed;
without Pillow the dishes/locations are created without images.
"""
import base64
import io
import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

API = os.environ["API_URL"].rstrip("/")
ROOT = Path(__file__).resolve().parent.parent
IMG = ROOT / "frontend" / "restaurant-frontend-app" / "src" / "assets" / "img"

LOCATIONS = [
    {
        "address": "48 Rustaveli Avenue",
        "description": "Our flagship restaurant in the heart of the city: seasonal greens, open kitchen and a big terrace.",
        "totalCapacity": 40,
        "averageOccupancy": "70%",
        "rating": 4.8,
        "image": "locations/rustaveli-avenue-48.jpg",
    },
    {
        "address": "14 Baratashvili Street",
        "description": "A cosy spot in the old town with a quiet courtyard. Perfect for family dinners.",
        "totalCapacity": 30,
        "averageOccupancy": "60%",
        "rating": 4.6,
        "image": "locations/baratashvili-street-14.jpg",
    },
    {
        "address": "9 Abashidze Street",
        "description": "Bright modern hall near the park, great for business lunches and group pre-orders.",
        "totalCapacity": 36,
        "averageOccupancy": "55%",
        "rating": 4.5,
        "image": "locations/abashidze-street-9.jpg",
    },
]

NUTRITION = {"weight": "350 g", "calories": "420 kcal", "proteins": "14 g", "fats": "18 g",
             "carbohydrates": "46 g", "vitamins": "A, C, K"}

DISHES = [
    ("Spring Salad", "Appetizer", 9.50, "Young greens, radish, cucumber and lemon-herb dressing.", "dishes/spring-salad.jpg"),
    ("Fresh Strawberry Mint Salad", "Appetizer", 8.90, "Strawberries, baby spinach, mint and balsamic glaze.", "dishes/fresh-strawberry-mint-salad.jpg"),
    ("Avocado Pine Nut Bowl", "MainCourse", 14.20, "Avocado, quinoa, roasted vegetables and toasted pine nuts.", "dishes/avocado-pine-nut-bowl.jpg"),
    ("Roasted Sweet Potato and Lentil Salad", "MainCourse", 13.40, "Warm sweet potato, green lentils, feta and pumpkin seeds.", "dishes/roasted-sweet-potato-and-lentil-salad.jpg"),
    ("Grilled Salmon with Greens", "MainCourse", 18.90, "Salmon fillet, asparagus and herb butter.", None),
    ("Mushroom Risotto", "MainCourse", 15.60, "Arborio rice, forest mushrooms and parmesan.", None),
    ("Caesar Salad", "Appetizer", 10.80, "Romaine, croutons, parmesan and classic Caesar dressing.", None),
    ("Tom Yum", "Appetizer", 12.20, "Spicy Thai soup with shrimps and coconut milk.", None),
    ("Berry Cheesecake", "Dessert", 7.50, "Creamy cheesecake with forest berries.", None),
    ("Chocolate Fondant", "Dessert", 8.20, "Warm chocolate cake with a molten centre and vanilla ice cream.", None),
    ("Mango Sorbet", "Dessert", 6.40, "Light and fresh, made in-house.", None),
]

TABLES_PER_LOCATION = [(1, 2), (2, 2), (3, 4), (4, 4), (5, 6), (6, 8)]  # (tableNumber, capacity)


def call(method, path, body=None, token=None, retries=4):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                raw = r.read().decode() or "null"
                return r.status, json.loads(raw)
        except urllib.error.HTTPError as e:
            text = e.read().decode(errors="replace")
            if e.code >= 500 and attempt < retries - 1:  # cold starts of .NET lambdas
                time.sleep(3)
                continue
            return e.code, text
        except (urllib.error.URLError, TimeoutError) as e:
            if attempt < retries - 1:
                time.sleep(3)
                continue
            raise SystemExit(f"{method} {path} failed: {e}")


def image_b64(rel):
    if not rel:
        return None
    try:
        from PIL import Image
    except ImportError:
        return None
    path = IMG / rel
    if not path.exists():
        return None
    im = Image.open(path).convert("RGB")
    im.thumbnail((1000, 1000))
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=80)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def create(path, payload, label):
    status, resp = call("POST", path, payload)
    if status in (200, 201):
        return resp
    if payload.get("imageBase64"):  # retry without the image rather than failing the whole seed
        print(f"  ! {label}: {status} with image, retrying without it")
        payload = {**payload, "imageBase64": None}
        status, resp = call("POST", path, payload)
        if status in (200, 201):
            return resp
    raise SystemExit(f"Failed to create {label}: {status} {resp}")


def main():
    status, existing = call("GET", "/locations")
    if status != 200:
        raise SystemExit(f"GET /locations returned {status}: {existing}")
    if existing and not os.environ.get("SEED_FORCE"):
        print(f"{len(existing)} location(s) already exist - skipping seed (set SEED_FORCE=1 to force).")
        return

    location_ids = []
    for loc in LOCATIONS:
        payload = {k: v for k, v in loc.items() if k != "image"}
        payload["imageBase64"] = image_b64(loc["image"])
        resp = create("/locations", payload, f"location {loc['address']}")
        loc_id = resp.get("id") or resp.get("Id")
        location_ids.append(loc_id)
        print(f"+ location {loc['address']} -> {loc_id}")

    for name, dish_type, price, desc, img in DISHES:
        payload = {"name": name, "description": desc, "dishType": dish_type, "price": price,
                   **NUTRITION, "imageBase64": image_b64(img), "state": "Available",
                   "locationIds": location_ids}
        resp = create("/dishes", payload, f"dish {name}")
        print(f"+ dish {name} -> {resp.get('id') or resp.get('Id')}")

    for loc_id in location_ids:
        for number, capacity in TABLES_PER_LOCATION:
            create("/tables", {"locationId": loc_id, "tableNumber": number, "capacity": capacity},
                   f"table {number} @ {loc_id}")
        print(f"+ {len(TABLES_PER_LOCATION)} tables @ {loc_id}")

    email, password = os.environ.get("WAITER_EMAIL"), os.environ.get("WAITER_PASSWORD")
    if email and password:
        status, resp = call("POST", "/register", {"firstName": "Demo", "lastName": "Waiter", "email": email,
                                                  "password": password, "confirmPassword": password})
        print(f"register waiter: {status}")
        status, resp = call("POST", "/waiters", {"email": email, "locationId": location_ids[0]})
        print(f"assign waiter to {location_ids[0]}: {status} {resp}")
    else:
        print("WAITER_EMAIL/WAITER_PASSWORD not set - no waiter account created.")

    print("Seed finished.")


if __name__ == "__main__":
    sys.exit(main())
